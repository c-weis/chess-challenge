using ChessChallenge.API;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    static int ExplorationDepth = 5;
    static int ExtraCaptureDepth = 1;
    static float[,,] WhitePieceValues;
    static Dictionary<ulong, (int, float)> PreviousEvaluations; // sends (board Zobrist key) => (depth, evaluation obtained previously at that depth)
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

        // Loop through moves and evaluate them
        float bestEval = float.NegativeInfinity;
        Move bestMove = sortedMoves.First();
        foreach(var move in sortedMoves){
            // adapt depth based on time remaining
            if(timer.MillisecondsRemaining > 10_000) {
                if(board.PlyCount < 10){
                    ExplorationDepth = 2;
                    ExtraCaptureDepth = 5;
                } else {
                    ExplorationDepth = 5;
                    ExtraCaptureDepth = 2;
                }
            }else{
                ExplorationDepth = 3;
                ExtraCaptureDepth = 1;
                if(timer.MillisecondsRemaining < 1_000) {
                    ExtraCaptureDepth = 0;
                }
            }

            // evaluate move to given depth
            board.MakeMove(move);
            var eval = - EvaluateRecursively(
                                    board, 
                                    ExplorationDepth-1,
                                    float.NegativeInfinity,
                                    -bestEval
                                    );
            if(eval > bestEval) {
                bestEval = eval;
                bestMove = move;
            }
            board.UndoMove(move);
        }

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

    public float EvaluateRecursively(Board board, int depth, 
                                            float bestPlayerEval, float bestOpponentEval){
        // Deal with trivial cases first: checkmate, draw
        if (board.IsInCheckmate()) return float.NegativeInfinity;
        if (board.IsDraw()) return 0;

        // See if we evaluated this position already at sufficient depth
        var zobristKey = board.ZobristKey;
        if(PreviousEvaluations.TryGetValue(zobristKey, out var depth_eval)){
            // we have seen this position before - check depth
            if(depth_eval.Item1 >= depth){
                return depth_eval.Item2;
            }
            // else carry on - the position needs to be reevaluated
        } else {
            // add default value so we can replace it later
            PreviousEvaluations.Add(zobristKey, default);
        }

        // Check if we need to stop the search for depth reasons
        if (depth == -ExtraCaptureDepth) return EvaluateBoard(board, depth, zobristKey);
        var moves = board.GetLegalMoves(depth<=0); // if depth is <= 0, get captures only
        if (!moves.Any()) return EvaluateBoard(board, depth, zobristKey);

        // Sort moves (if remaining depth > 1)
        var sortedMoves = (depth > 1) ? moves.OrderByDescending(move => MoveEvaluationOrder(board, move)) 
                                        : moves.AsEnumerable();

        // Loop through moves, recursively calling this function and employing alpha-beta pruning
        float bestEval = float.NegativeInfinity;
        foreach(var move in sortedMoves){
            board.MakeMove(move);
            var eval = - EvaluateRecursively(
                                    board, 
                                    depth-1,
                                    -bestOpponentEval,
                                    -bestPlayerEval
                                    );
            board.UndoMove(move);
            if(eval > bestEval) {
                bestPlayerEval = Math.Max(bestPlayerEval, eval);
                bestEval = eval;
                if(bestEval > bestOpponentEval) break;
            }
        }

        // Store evaluation in dictionary - we made sure the key exists earlier
        PreviousEvaluations[zobristKey] = (depth, bestEval);

        return bestEval;
    }


    private float EvaluateBoard(Board board, int storageDepth, ulong zobristKey) {
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

        PreviousEvaluations[zobristKey] = (storageDepth, totalEvaluation);
        
        return totalEvaluation;
    }
}