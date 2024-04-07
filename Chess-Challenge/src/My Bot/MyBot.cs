#define USE_COMPUTATION_TABLE
using ChessChallenge.API;
using ChessChallenge.Debugging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class MyBot : IChessBot
{
    // Parameters for Sensibot
    static int MaxExplorationDepth = 3; 
    static int MaxExtraCaptureDepth = 3;
    private int ExplorationDepth = MaxExplorationDepth;
    private int ExtraCaptureDepth = MaxExtraCaptureDepth;

    // Parameters for Play bot
    private int BotVisionDepth = 2; 
    private int PlayDepth = 2;
    static float StrategicalDiscrepancy = 0.5f;

    // Other variables
    static float CheckMateValue = 1e6f; // Large, but finite value for checkmate.
    static float[,,] WhitePieceValues;
    public Dictionary<ulong, (float Evaluation, Move BestMove, int Depth)> EvaluationTable {get; set;} = new(); 
    // sends (board Zobrist key) => (evaluation of the position, best move, depth of the search that found this (can be longer than actual line))
    public int BoardEvaluationCounter {get; private set;} = 0;
    static int ActivatedBits(ulong bitboard) => BitboardHelper.GetNumberOfSetBits(bitboard);

    static MyBot(){
        // create lookup tables for piece values
        WhitePieceValues = new float[5,8,8];

        // values before centre bonus
        foreach(var file in Enumerable.Range(0,8)){
            foreach(var rank in Enumerable.Range(0,8)){
                // Pawn values
                WhitePieceValues[0, file, rank] = 1.0f + rank * 0.005f;
                // Knight values
                WhitePieceValues[1, file, rank] = 3.0f + (rank != 0 ? 0.005f : 0);
                // Bishop values
                WhitePieceValues[2, file, rank] = 3.3f + (rank != 0 ? 0.005f : 0);
                // Rook values
                WhitePieceValues[3, file, rank] = 5.0f + (rank != 0 ? 0.003f : 0);
                // Queen values
                WhitePieceValues[4, file, rank] = 9.0f + (rank != 0 ? 0.001f : 0);
            }
        }

        // add centre bonus
        // pawns and knights
        foreach (var rank in Enumerable.Range(2, 4))
        {
            foreach (var file in Enumerable.Range(2, 4))
            {
                /* Value central 4 + 8 squares:
                 *  V = given centre bonus 
                 *  x = V/2
                 *
                 *      x x
                 *    x V V v
                 *    x V V x
                 *      x x
                 */
                float value = 0.01f * (3.0f - (Math.Abs(rank - 3.5f) + Math.Abs(rank - 3.5f)))/3.0f;
                WhitePieceValues[0, rank, file] += value; // pawns
                WhitePieceValues[1, rank, file] += 0.02f;
            }
        }

    }

    bool beSensible = false;
    public Move ThinkBoth(Board board, Timer timer)
    {
        float bestSensiEval = float.NegativeInfinity;
        var moves = board.GetLegalMoves();
        var moveEvals = new Dictionary<Move, float>();

        DepthDecider(board, timer);

        // First go through all moves and find the ones that, according to SensiBot, are within StrategicalDiscrepancy of the best move
        foreach (var move in moves)
        {
            board.MakeMove(move);

            // Evaluate position recursively. Here the upper cutoff is increased by StrategicalDiscrepancy to not discard near top moves
            float eval = -EvaluateRecursively(board, ExplorationDepth-1, float.NegativeInfinity, -(bestSensiEval-StrategicalDiscrepancy)).Evaluation;
            moveEvals.Add(move, eval);
            bestSensiEval = Math.Max(eval, bestSensiEval);

            board.UndoMove(move);
        }

        Move bestMove = Move.NullMove;
        float bestPlayEval = float.NegativeInfinity;
        float howSensible = float.NegativeInfinity;

        int numberOfMovesPassedOn = 0;

        foreach(var moveEval in moveEvals) {
            // only consider moves withing StrategicalDiscrepancy of the top move
            if (moveEval.Value < bestSensiEval - StrategicalDiscrepancy) continue;

            numberOfMovesPassedOn++;

            board.MakeMove(moveEval.Key);

            ExtraCaptureDepth = 2;
            float eval = -EvaluateByPlaying(board, BotVisionDepth, PlayDepth);

            if (eval > bestPlayEval)
            {
                bestMove = moveEval.Key;
                bestPlayEval = eval;
                howSensible = moveEval.Value;
            }

            board.UndoMove(moveEval.Key);
        }

        Debug.WriteLine($"{bestPlayEval:0.00},  ({howSensible:0.00} < {bestSensiEval:0.00}) among {numberOfMovesPassedOn} moves");

        return bestMove;
    }

    public Move Think(Board board, Timer timer)
    {
        // Clear lookup table
        EvaluationTable.Clear();

        // adapt depth based on time remaining
        DepthDecider(board, timer);

        // Reset counter of board evaluations
        BoardEvaluationCounter = 0;

        // On the top level we still check the table, mainly to save the top level computations.
        var LastComputation = EvaluateRecursively(
                                    board, 
                                    ExplorationDepth,
                                    float.NegativeInfinity,
                                    float.PositiveInfinity,
                                    outputComputationSummaries: false
                                    );

        // extend search in end game - sloppy implementation for now
        int totalNumberOfPieces = ActivatedBits(board.AllPiecesBitboard);
        while (
            totalNumberOfPieces < 10
            && timer.MillisecondsRemaining > 50 * timer.MillisecondsElapsedThisTurn
            && LastComputation.Evaluation < CheckMateValue
            )
        {
            ExplorationDepth += 1;
            ExtraCaptureDepth += 1;
            LastComputation = EvaluateRecursively(
                                        board, 
                                        ExplorationDepth,
                                        float.NegativeInfinity,
                                        float.PositiveInfinity,
                                        outputComputationSummaries: false
                                        );
        }

        return LastComputation.Item2;
    }

    private static int MoveEvaluationOrder(Board board, Move move){
        board.MakeMove(move);

        var order = -board.GetLegalMoves().Length;
        order += board.IsInCheckmate() ? 1000 : 0;
        order += board.IsInCheck() ? 500 : 0;
        order += move.IsCapture ? 250 : 0;

        board.UndoMove(move);
        return order;
    }

    // EvaluateRecursively searches for the best move on board up to depth, discarding any moves that are below minEval or above maxEval
    public (float Evaluation, Move BestMove) EvaluateRecursively(Board board, int depth,
                                            float lowerCutoff, float upperCutoff,
                                            bool outputComputationSummaries = false)
    {
#if USE_COMPUTATION_TABLE
        // First, see if we evaluated this position already at sufficient depth
        // XOR with plycount to account for repetitions (need to left shift plycount because last bit already xors with IsWhiteToMove)
        var zobristKey = board.ZobristKey ^ ((ulong)board.PlyCount << 1);
        // Every evaluation is stored from white's perspective

        bool keyExisted = false;
        if (EvaluationTable.TryGetValue(zobristKey, out var previousEval))
        {
            // we have seen this position before - check depth
            if (previousEval.Depth >= depth)
            {
                return (previousEval.Evaluation, previousEval.BestMove);
            }
            keyExisted = true;
            // else carry on - the position needs to be reevaluated
        }
#endif

        // Deal with trivial cases first: checkmate, draw
        if (board.IsInCheckmate()) return (-CheckMateValue * (1000 - board.PlyCount), Move.NullMove);
        if (board.IsDraw()) return (0, Move.NullMove);

        // Check if we need to stop the search for depth reasons
        if (depth == -ExtraCaptureDepth) return (EvaluateBoard(board), Move.NullMove);
        var moves = board.GetLegalMoves();

        // Sort moves (if remaining depth >= 0)
        var sortedMoves = (depth > 0) ? moves.OrderByDescending(move => MoveEvaluationOrder(board, move))
                                      : moves.AsEnumerable();

        var bestComputation = ((depth > 0) ? float.NegativeInfinity : EvaluateBoard(board), Move.NullMove);

        // Loop through moves, recursively calling this function and employing alpha-beta pruning
        foreach (var move in sortedMoves)
        {
            // at depth <= 0, only look for captures and checks! (we're not sorting at this depth so can do this here)
            if (depth <= 0 && !move.IsCapture && !board.IsInCheck()) continue;

            board.MakeMove(move);
            // Get recursive evalutation
            var recursiveEval = EvaluateRecursively(
                                    board,
                                    depth - 1,
                                    -upperCutoff,
                                    -lowerCutoff
                                    );
            board.UndoMove(move);

            // Don't further consider evaluations that were interrupted by a cutoff
            if (float.IsNaN(recursiveEval.Evaluation)) continue;
            // Adjust evalutation to current players perspective
            recursiveEval.Evaluation *= -1;


            // Disabled output summaries (the old version still uses the struct Computation)
            // if (outputComputationSummaries) { Debugger.OutputSummary(computation, board); }

            if (recursiveEval.Evaluation > bestComputation.Item1)
            {
                lowerCutoff = Math.Max(lowerCutoff, recursiveEval.Evaluation);
                bestComputation = (recursiveEval.Evaluation, move);

                if (lowerCutoff > upperCutoff)
                {
                    // too good to be true. this should not be saved in the PreviousEvaluations
                    bestComputation = (float.NaN, Move.NullMove);
                    break;
                }
            }
        }

#if USE_COMPUTATION_TABLE
        if (!float.IsNaN(bestComputation.Item1))
        {
            // This evaluation was not cut off and can be trusted, so we save it together with the current depth
            if (!keyExisted) EvaluationTable.Add(zobristKey, default);
            EvaluationTable[zobristKey] = (bestComputation.Item1, bestComputation.Item2, depth);
        }
#endif

        return bestComputation;
    }

    private float EvaluateByPlaying(Board board, int botVisionDepth, int numberOfPlies)
    {
        var computation = EvaluateRecursively(board,
                                              botVisionDepth,
                                              float.NegativeInfinity,
                                              float.PositiveInfinity);

        if (numberOfPlies == 0 || computation.BestMove == Move.NullMove) return computation.Evaluation;

        board.MakeMove(computation.BestMove);
        var evaluation = -EvaluateByPlaying(board, botVisionDepth, numberOfPlies-1);
        board.UndoMove(computation.BestMove);

        return evaluation;
    }

    private float EvaluateBoard(Board board) {
        var evaluationForWhite = 0.0f;
        var pieceLists = board.GetAllPieceLists();

        foreach(int i in Enumerable.Range(0, 5)){
            // White piece values
            foreach(var piece in pieceLists[i])
                evaluationForWhite += WhitePieceValues[i,piece.Square.File,piece.Square.Rank];
            // Black piece values (flip board vertically)
            foreach(var piece in pieceLists[6+i])
                evaluationForWhite -= WhitePieceValues[i,piece.Square.File,7-piece.Square.Rank];
        }

        // King safety
        evaluationForWhite += KingSafetyValue(board, pieceLists[5][0].Square)
                            - KingSafetyValue(board, pieceLists[11][0].Square);

        var totalEvaluation = 
            (board.IsWhiteToMove ? 1.0f : -1.0f) * evaluationForWhite
            - (board.IsInCheck() ? 1e-5f : 0.0f); // bias against being in check

        // We have evaluated another board, increase counter
        BoardEvaluationCounter++;
        return totalEvaluation;
    }

    private float KingSafetyValue(Board board, Square kingSquare)
    {
        float sum = 0.0f;
        ulong kingAdjacentSquares = BitboardHelper.GetKingAttacks(kingSquare);

        // Subtract score for each square around the king under attack
        while (kingAdjacentSquares != 0)
        {
            int index = BitboardHelper.ClearAndGetIndexOfLSB(ref kingAdjacentSquares);
            if (board.SquareIsAttackedByOpponent(new Square(index)))
            {
                sum -= 0.05f;
            }
        }
        return sum;
    }

    private void DepthDecider(Board board, Timer timer)
    {
        ExplorationDepth = MaxExplorationDepth;
        ExtraCaptureDepth = MaxExtraCaptureDepth;
        if (timer.MillisecondsRemaining > 10_000)
        {
            if (board.PlyCount < 10)
            {
                ExplorationDepth = MaxExplorationDepth - 1;
            }
            else
            {
                ExplorationDepth = MaxExplorationDepth;
            }
        }
        else
        {
            ExplorationDepth = MaxExplorationDepth - 1;
            if (timer.MillisecondsRemaining < 5_000)
            {
                ExtraCaptureDepth = MaxExtraCaptureDepth - 2;
            }
            else if (timer.MillisecondsRemaining < 1_000)
            {
                ExplorationDepth = MaxExplorationDepth - 2;
            }
        }
        // Enforce maxima and make sure the depths are positive
        ExplorationDepth = Math.Clamp(ExplorationDepth, 0, MaxExplorationDepth);
        // Ensure the sum of exploration depth and capture depth stays even
        if (ExtraCaptureDepth == 0)
        {
            ExtraCaptureDepth = ExplorationDepth % 2;
        }
        else
        {
            ExtraCaptureDepth = Math.Clamp(ExtraCaptureDepth, 0, MaxExtraCaptureDepth) - (ExplorationDepth + ExtraCaptureDepth) % 2;
        }
    }
}
