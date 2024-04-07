#define USE_COMPUTATION_TABLE
using ChessChallenge.API;
using ChessChallenge.Debugging;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ChessChallenge.EvilBots;
public class SensiBot: IChessBot
{
    static int MaxExplorationDepth = 3; 
    static int MaxExtraCaptureDepth = 5;
    private int ExplorationDepth = MaxExplorationDepth;
    private int ExtraCaptureDepth = MaxExtraCaptureDepth;
    static float CheckMateValue = 1e6f; // Large, but finite value for checkmate.
    static float[,,] WhitePieceValues;
    private Dictionary<ulong, Computation> PreviousEvaluations {get; set;} = new(); // sends (board Zobrist key) => Computation
    public Computation LastComputation {get; set;}
    public int BoardEvaluationCounter {get; private set;} = 0;
    public int RunningAverageBoardEvaluations {get; private set;} = -1;
    static int ActivatedBits(ulong bitboard) => BitboardHelper.GetNumberOfSetBits(bitboard);

    static SensiBot(){
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

    public Move Think(Board board, Timer timer)
    {
        // Clear lookup table
        PreviousEvaluations.Clear();

        // adapt depth based on time remaining
        DepthDecider(board, timer);

        // Reset counter of board evaluations
        BoardEvaluationCounter = 0;

        // On the top level we still check the table, mainly to save the top level computations.
        LastComputation = EvaluateRecursively(
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
                                        outputComputationSummaries: true
                                        );
        }

        RunningAverageBoardEvaluations = (9*RunningAverageBoardEvaluations + BoardEvaluationCounter)/10;

        return LastComputation.BestMove;
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
    public Computation EvaluateRecursively(Board board, int depth,
                                            float lowerCutoff, float upperCutoff,
                                            bool outputComputationSummaries = false)
    {
#if USE_COMPUTATION_TABLE
        // First, see if we evaluated this position already at sufficient depth
        // XOR with plycount to account for repetitions
        var zobristKey = board.ZobristKey ^ ((ulong)board.PlyCount << 1);
        // Every evaluation is stored from white's perspective

        bool keyExisted = false;
        if (PreviousEvaluations.TryGetValue(zobristKey, out var previousEval))
        {
            // we have seen this position before - check depth
            if (previousEval.Depth >= depth)
            {
                return previousEval;
            }
            keyExisted = true;
            // else carry on - the position needs to be reevaluated
        }
#endif

        // Deal with trivial cases first: checkmate, draw
        if (board.IsInCheckmate()) return new Computation(-CheckMateValue * (1000 - board.PlyCount), 100);
        if (board.IsDraw()) return new Computation(0, 100);

        // Check if we need to stop the search for depth reasons
        if (depth == -ExtraCaptureDepth) return new Computation(EvaluateBoard(board), depth);
        var moves = board.GetLegalMoves();

        // Sort moves (if remaining depth >= 0)
        var sortedMoves = (depth > 0) ? moves.OrderByDescending(move => MoveEvaluationOrder(board, move))
                                      : moves.AsEnumerable();

        float bestEval = (depth > 0) ? float.NegativeInfinity : EvaluateBoard(board);
        Computation bestComputation = new(bestEval, depth); // This should never be used if depth > 0

        // Loop through moves, recursively calling this function and employing alpha-beta pruning
        foreach (var move in sortedMoves)
        {
            board.MakeMove(move);

            // at depth <= 0, only look for captures and checks! (we're not sorting at this depth so can do this here)
            if (depth <= 0 && !move.IsCapture && !board.IsInCheck())
            {
                board.UndoMove(move);
                continue;
            }

            var computation = EvaluateRecursively(
                                    board,
                                    depth - 1,
                                    -upperCutoff,
                                    -lowerCutoff
                                    );

            computation = computation.Extend(move, depth);
            board.UndoMove(move);

            if (outputComputationSummaries)
            {
                // Disabled because incompatibility. (Debugger no longer uses struct Computation)
                // Debugger.OutputSummary(computation, board);
                Debug.WriteLine($"SensiBot {computation.Evaluation:0.00} ({computation.Depth}+{computation.ExtraDepth})");
            }

            if (computation.Evaluation >= bestEval)
            {
                lowerCutoff = Math.Max(lowerCutoff, computation.Evaluation);
                bestEval = computation.Evaluation;
                bestComputation = computation;
                if (lowerCutoff > upperCutoff)
                {
                    // too good to be true. this should not be saved in the PreviousEvaluations
                    break;
                }
            }
        }

#if USE_COMPUTATION_TABLE
        if (lowerCutoff <= bestComputation.Evaluation && bestComputation.Evaluation <= upperCutoff)
        {
            // This evaluation was not cut off and can be trusted, so we save it
            if (!keyExisted) PreviousEvaluations.Add(zobristKey, default);
            PreviousEvaluations[zobristKey] = bestComputation;
        }
#endif

        return bestComputation;
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


public struct Computation {

    public List<Move> Line;
    public float Evaluation {get; set;}
    public int Depth {get; set; }
    public int ExtraDepth {get; set; }
    public readonly Move BestMove => Line.LastOrDefault(Move.NullMove);

    public Computation(float evaluation, int depth){
        Evaluation = evaluation;
        Depth = Math.Min(depth,0);
        ExtraDepth = Math.Max(-depth,0);
        Line = new List<Move>();
    }

    public Computation Extend(Move move, int currentDepth) {
        var extendedEval = new Computation(-Evaluation, currentDepth)
        {
            ExtraDepth = ExtraDepth,
            Line = new(Line) // copy line
        };
        extendedEval.Line.Add(move); //add new move
        extendedEval.Depth = currentDepth;
        return extendedEval;
    }
}

