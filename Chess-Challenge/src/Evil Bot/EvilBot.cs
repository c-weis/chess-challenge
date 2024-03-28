using ChessChallenge.API;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChessChallenge.EvilBots;
public class EvilBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        return BestMoveRecursive(board, 4).Item1;
    }

    public (Move, float) BestMoveRecursive(Board board, int depth){
        if (board.IsInCheckmate()) return (default, float.NegativeInfinity);
        if (board.IsDraw()) return (default, 0);
        if (depth == 0) return (default, EvaluateBoard(board));

        float max = float.NegativeInfinity;
        Move bestMove = board.GetLegalMoves()[0];
        foreach(var move in board.GetLegalMoves()){
            board.MakeMove(move);
            var eval = - BestMoveRecursive(board, depth-1).Item2;
            if(eval > max) {
                max = eval;
                bestMove = move;
            }
            board.UndoMove(move);
        }

        return (bestMove, max);
    }

    static List<float> values = new (){0.0f, 1.0f, 3.0f, 3.3f, 5.0f, 9.0f, 100.0f};

    private float EvaluateBoard(Board board) {
        var sum = 0.0f;
        var pieceLists = board.GetAllPieceLists();
        foreach(var pieceList in pieceLists){
            var multiplier = pieceList.IsWhitePieceList ? 1.0f : -1.0f;
            sum += multiplier * pieceList.Count * values[(int)pieceList.TypeOfPieceInList];
        }
        
        return Multiplier(board) * sum;
    }
    private float Multiplier(Board board){
        return board.IsWhiteToMove ? 1.0f : -1.0f;
    }

}