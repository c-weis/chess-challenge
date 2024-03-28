using ChessChallenge.API;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    static int ExplorationDepth = 3;
    static int ExtraCaptureDepth = 1;
    static float[,,] WhitePieceValues;
    static Dictionary<ulong, (int, float, Move)> PreviousEvaluations; // sends (board Zobrist key) => (depth, evaluation obtained previously at that depth, best Move)
    static MyBot(){
        // create lookup tables for piece values
        WhitePieceValues = new float[5,8,8];

        // values before centre bonus
        foreach(var file in Enumerable.Range(0,8)){
            foreach(var rank in Enumerable.Range(0,8)){
                // Pawn values
                WhitePieceValues[0, file, rank] = 1.0f + rank * 1e-3f;
                // Knight values
                WhitePieceValues[1, file, rank] = 3.0f + (rank != 0 ? 1e-3f : 0);
                // Bishop values
                WhitePieceValues[2, file, rank] = 3.3f + (rank != 0 ? 1e-3f : 0);
                // Rook values
                WhitePieceValues[3, file, rank] = 5.0f + (rank != 0 ? 1e-3f : 0);
                // Queen values
                WhitePieceValues[4, file, rank] = 9.0f + (rank != 0 ? 1e-3f : 0);
            }
        }

        // add centre bonus
        // pawns and knights
        foreach (var pieceID in Enumerable.Range(0,2)){
            WhitePieceValues[pieceID,3,3] += 1e-2f;
            WhitePieceValues[pieceID,3,4] += 1e-2f;
            WhitePieceValues[pieceID,4,3] += 1e-2f;
            WhitePieceValues[pieceID,4,4] += 1e-2f;
        }

        PreviousEvaluations = new();
    }

    public Move Think(Board board, Timer timer)
    {
        // FIND BEST MOVE
        // Fetch allowed moves and sort them
        var sortedMoves = board.GetLegalMoves().OrderByDescending(move => MoveEvaluationOrder(board, move));

//        // adapt depth based on time remaining
//        if(timer.MillisecondsRemaining > 10_000) {
//            if(board.PlyCount < 10){
//                ExplorationDepth = 2;
//                ExtraCaptureDepth = 5;
//            } else {
//                ExplorationDepth = 5;
//                ExtraCaptureDepth = 2;
//            }
//        }else{
//            ExplorationDepth = 3;
//            ExtraCaptureDepth = 1;
//            if(timer.MillisecondsRemaining < 1_000) {
//                ExtraCaptureDepth = 0;
//            }
//        }

        Move bestMove;
        // On the top level we don't check the table
        bestMove = EvaluateCheckTable(
                                    board, 
                                    ExplorationDepth,
                                    float.NegativeInfinity,
                                    float.PositiveInfinity
                                    ).Item2;

        return bestMove;
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
    public (float, Move) EvaluateRecursively(Board board, int depth, 
                                            float lowerCutoff, float upperCutoff){
        // Deal with trivial cases first: checkmate, draw
        if (board.IsInCheckmate()) return (float.NegativeInfinity, default);
        if (board.IsDraw()) return (0, default);

        // Check if we need to stop the search for depth reasons
        if (depth == -ExtraCaptureDepth)  return (EvaluateBoard(board), default); 
        var moves = board.GetLegalMoves(depth<=0); // if depth is <= 0, get captures only
        if (!moves.Any()) return (EvaluateBoard(board), default);

        // Sort moves (if remaining depth >= 2)
        var sortedMoves = (depth >= 2) ? moves.OrderByDescending(move => MoveEvaluationOrder(board, move)) 
                                        : moves.AsEnumerable();

        float bestEval = float.NegativeInfinity;
        Move bestMove = moves.First();
        // Loop through moves, recursively calling this function and employing alpha-beta pruning
        foreach(var move in sortedMoves){
            board.MakeMove(move);
            var eval = - EvaluateCheckTable(
                                    board, 
                                    depth-1,
                                    -upperCutoff,
                                    -lowerCutoff
                                    ).Item1;
            board.UndoMove(move);
            if(eval > bestEval) {
                lowerCutoff = Math.Max(lowerCutoff, eval);
                bestEval = eval;
                bestMove = move;
                if(lowerCutoff > upperCutoff) {
                    // too good to be true. this should not be saved in the PreviousEvaluations
                    break;
                }
            }
        }

        return (bestEval, bestMove);
    }

    public (float, Move) EvaluateCheckTable(Board board, int depth, 
                                            float lowerCutoff, float upperCutoff){
        // First, see if we evaluated this position already at sufficient depth
        // XOR with plycount to account for repetitions
        var zobristKey = board.ZobristKey ^ (ulong)(board.PlyCount);
        // Every evaluation is stored from white's perspective
        var whiteMultiplier = (board.IsWhiteToMove ? 1.0f : -1.0f);

        Boolean keyExisted = false;
        if(PreviousEvaluations.TryGetValue(zobristKey, out var depth_eval)){
            // we have seen this position before - check depth
            if(depth_eval.Item1 >= depth){
                return (depth_eval.Item2 * whiteMultiplier, depth_eval.Item3);
            }
            keyExisted = true;
            // else carry on - the position needs to be reevaluated
        } 

        var (bestEval,bestMove) = EvaluateRecursively(board, depth, lowerCutoff, upperCutoff);
        if (lowerCutoff <= bestEval && bestEval <= upperCutoff) {
            // This evaluation was not cutoff and can be trusted, so we save it.
            if (!keyExisted) PreviousEvaluations.Add(zobristKey, default);
            PreviousEvaluations[zobristKey] = (depth, bestEval*whiteMultiplier, bestMove);
        }
        return (bestEval, bestMove);
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

        return totalEvaluation;
    }
}