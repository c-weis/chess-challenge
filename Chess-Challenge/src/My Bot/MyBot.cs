using ChessChallenge.API;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    static int ExtraCaptureDepth = 2;
    public Move Think(Board board, Timer timer)
    {
        return BestMoveRecursive(board, 4, float.PositiveInfinity).Item1;
    }

    private static int MoveOrder(Board board, Move move){
        board.MakeMove(move);

        var order = -board.GetLegalMoves().Length;
        order += board.IsInCheckmate() ? 100 : 0;
        order += board.IsInCheck() ? 50 : 0;
        order += move.IsCapture ? 25 : 0;

        board.UndoMove(move);
        return order;
    }

    public (Move, float) BestMoveRecursive(Board board, int depth, float cutoff){
        if (board.IsInCheckmate()) return (default, float.NegativeInfinity);
        if (board.IsDraw()) return (default, 0);
        if (depth == -ExtraCaptureDepth) return (default, EvaluateBoard(board));

        var moves = board.GetLegalMoves(depth<=0); // if depth is <= 0, get captures only
        if (!moves.Any()) return (default, EvaluateBoard(board));
        var sortedMoves = moves.OrderByDescending(move => MoveOrder(board, move));

        float bestEval = float.NegativeInfinity;
        Move bestMove = sortedMoves.First();
        foreach(var move in sortedMoves){
            board.MakeMove(move);
            var eval = - BestMoveRecursive(
                                    board, 
                                    depth-1,
                                    -bestEval
                                    ).Item2;
            if(eval > bestEval) {
                bestEval = eval;
                bestMove = move;
                if(bestEval > cutoff){
                    board.UndoMove(move);
                    return (default, eval);
                }
            }
            board.UndoMove(move);
        }

        return (bestMove, bestEval);
    }

    private float EvaluatePawn(Piece piece){
        Square square = piece.Square;
        float centreBonus = 7.0f - (Math.Abs(square.Rank-3.5f) + Math.Abs(square.File-3.5f));
        if(piece.IsWhite) { // add promotionBonus
            return 1.0f + centreBonus/50.0f + square.Rank/100.0f;
        }
        return 1.0f + centreBonus/50.0f + (7.0f - square.Rank)/100.0f;
    }
    private float EvaluateKnight(Piece piece){
        Square square = piece.Square;
        float centreBonus = 7.0f - (Math.Abs(square.Rank-3.5f) + Math.Abs(square.File-3.5f));
        return 3.0f + centreBonus/50.0f;
    }
    private float EvaluateBishop(Piece piece){
        return 3.3f;
    }
    private float EvaluateRook(Piece piece){
        return 5.0f;
    }
    private float EvaluateQueen(Piece piece){
        return 9.0f;
    }

    private float EvaluateBoard(Board board) {
        var sum = 0.0f;
        var pieceLists = board.GetAllPieceLists();

        sum += pieceLists[0].Sum(EvaluatePawn);
        sum += pieceLists[1].Sum(EvaluateKnight);
        sum += pieceLists[2].Sum(EvaluateBishop);
        sum += pieceLists[3].Sum(EvaluateRook);
        sum += pieceLists[4].Sum(EvaluateQueen);
        sum -= pieceLists[6].Sum(EvaluatePawn);
        sum -= pieceLists[7].Sum(EvaluateKnight);
        sum -= pieceLists[8].Sum(EvaluateBishop);
        sum -= pieceLists[9].Sum(EvaluateRook);
        sum -= pieceLists[10].Sum(EvaluateQueen);

        return Multiplier(board) * sum;
    }

    private float Multiplier(Board board){
        return board.IsWhiteToMove ? 1.0f : -1.0f;
    }

}