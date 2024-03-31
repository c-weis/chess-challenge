using ChessChallenge.API;
using ChessChallenge.Debugging;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    static int ExplorationDepth = 3;
    static int ExtraCaptureDepth = 5;
    static float CheckMateValue = 1e6f; // Large, but finite value for checkmate.
    static float[,,] WhitePieceValues;
    private Dictionary<ulong, Computation> PreviousEvaluations {get; set;} = new(); // sends (board Zobrist key) => Computation
    public Computation LastComputation {get; set;}
    public int BoardEvaluationCounter {get; private set;} = 0;
    public int RunningAverageBoardEvaluations {get; private set;} = -1;

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
            foreach (var rank in Enumerable.Range(3,5)){
                foreach (var file in Enumerable.Range(3,4)){
                    WhitePieceValues[0,rank,file] += 0.01f;
                    WhitePieceValues[1,rank,file] += 0.02f;
                }
            }

    }

    public Move Think(Board board, Timer timer)
    {
        // adapt depth based on time remaining
        DepthDecider(board, timer);

        // Reset counter of board evaluations
        BoardEvaluationCounter = 0;

        // On the top level we still check the table, mainly to save the top level computations.
        LastComputation = EvaluateCheckTable(
                                    board, 
                                    ExplorationDepth,
                                    float.NegativeInfinity,
                                    float.PositiveInfinity,
                                    outputComputationSummaries: false
                                    );

        // LastComputation = EvaluateRecursively(
        //                             board, 
        //                             ExplorationDepth,
        //                             float.NegativeInfinity,
        //                             float.PositiveInfinity,
        //                             outputComputationSummaries: false,
        //                             useComputationTable: false
        //                             );

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
                                            bool outputComputationSummaries=false,
                                            bool useComputationTable=false){
        // Deal with trivial cases first: checkmate, draw
        if (board.IsInCheckmate()) return new Computation(-CheckMateValue*(10+depth), 100);
        if (board.IsDraw()) return new Computation(0, 100);

        // Check if we need to stop the search for depth reasons
        if (depth == -ExtraCaptureDepth)  return new Computation(EvaluateBoard(board), depth); 
        var moves = board.GetLegalMoves(depth<=0); // if depth is <= 0, get captures only
        if (!moves.Any()) return new Computation(EvaluateBoard(board), depth);

        // Sort moves (if remaining depth >= 0)
        var sortedMoves = (depth >= 0) ? moves.OrderByDescending(move => MoveEvaluationOrder(board, move)) 
                                        : moves.AsEnumerable();

        float bestEval = float.NegativeInfinity;
        Computation bestComputation = new(float.NegativeInfinity,0);

        // Loop through moves, recursively calling this function and employing alpha-beta pruning
        foreach(var move in sortedMoves){
            board.MakeMove(move);
            var computation = (useComputationTable ? 
                                EvaluateCheckTable(
                                    board, 
                                    depth-1,
                                    -upperCutoff,
                                    -lowerCutoff,
                                    outputComputationSummaries:false
                                    )
                                :
                                EvaluateRecursively(
                                    board, 
                                    depth-1,
                                    -upperCutoff,
                                    -lowerCutoff,
                                    outputComputationSummaries,
                                    useComputationTable : false
                                    ));
            computation = computation.Extend(move, depth);
            board.UndoMove(move);

            if(outputComputationSummaries) {
                Debugger.OutputSummary(computation, board);
            }

            if(computation.Evaluation >= bestEval) {
                lowerCutoff = Math.Max(lowerCutoff, computation.Evaluation);
                bestEval = computation.Evaluation;
                bestComputation = computation;
                if(lowerCutoff > upperCutoff) {
                    // too good to be true. this should not be saved in the PreviousEvaluations
                    break;
                }
            }
        }

        return bestComputation;
    }

    public Computation EvaluateCheckTable(Board board, int depth, 
                                            float lowerCutoff, float upperCutoff,
                                            bool outputComputationSummaries=false){

        // First, see if we evaluated this position already at sufficient depth
        // XOR with plycount to account for repetitions
        var zobristKey = board.ZobristKey ^ ((ulong)board.PlyCount << 1);
        // Every evaluation is stored from white's perspective

        bool keyExisted = false;
        if(PreviousEvaluations.TryGetValue(zobristKey, out var previousEval)){
            // we have seen this position before - check depth
            if(previousEval.Depth >= depth){
                return previousEval;
            }
            keyExisted = true;
            // else carry on - the position needs to be reevaluated
        } 

        var computation = EvaluateRecursively(board, depth, lowerCutoff, upperCutoff, outputComputationSummaries);

        if (lowerCutoff <= computation.Evaluation && computation.Evaluation <= upperCutoff) {
            // This evaluation was not cut off and can be trusted, so we save it
            if (!keyExisted) PreviousEvaluations.Add(zobristKey, default);
            PreviousEvaluations[zobristKey] = computation;
        }
        return computation;
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

        var totalEvaluation = 
            (board.IsWhiteToMove ? 1.0f : -1.0f) * evaluationForWhite
            - (board.IsInCheck() ? 1e-5f : 0.0f); // bias against being in check

        // We have evaluated another board, increase counter
        BoardEvaluationCounter++;
        return totalEvaluation;
    }

    private void DepthDecider(Board board, Timer timer){
        if(timer.MillisecondsRemaining > 10_000) {
            if(board.PlyCount < 10){
                ExplorationDepth = 3;
            } else {
                ExplorationDepth = 4;
            }
        }else{
            ExplorationDepth = 3;
            if(timer.MillisecondsRemaining < 1_000) {
                ExtraCaptureDepth = 5;
            }
            else if (timer.MillisecondsRemaining < 100) {
                ExplorationDepth = 2;
            }
        }
        return;
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