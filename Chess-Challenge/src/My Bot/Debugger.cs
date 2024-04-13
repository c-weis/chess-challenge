using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Resources;
using System.Text;
using ChessChallenge.Chess;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ChessChallenge.Debugging;
static class Debugger{
    public static void OutputLatestComputationStats(MyBot bot, Chess.Board board){
        Debug.WriteLine($"{Summary(bot.EvaluationTable, board)}| {bot.BoardEvaluationCounter:0}");
    }    

    public static void OutputSummary(Dictionary<ulong, (float Evaluation, API.Move BestMove, int Depth)> EvaluationTable, API.Board board){
        var chessBoard = new Chess.Board(board.SecretInternalBoard);
        Debug.WriteLine($"{Summary(EvaluationTable, chessBoard)}");
    }
//
//    public static StringBuilder Summary(Dictionary<ulong, (float Evaluation, Move BestMove, int Depth)> EvaluationTable, API.Board board) {
//        return Summary(EvaluationTable, new Chess.Board(board.SecretInternalBoard));
//    }

    public static StringBuilder Summary(Dictionary<ulong, (float Evaluation, API.Move BestMove, int Depth)> EvaluationTable, Chess.Board board) {
        ulong zobristKey = board.ZobristKey ^ ((ulong)board.plyCount << 1);
        // var startingEval = EvaluationTable[zobristKey];
        if (!EvaluationTable.TryGetValue(zobristKey, out var startingEval)) return new StringBuilder("no key found");
        StringBuilder stringBuilder = new(
            $"{EvaluationToString(startingEval.Evaluation, board.plyCount)} ({startingEval.Depth}) "
        );
        if (!board.IsWhiteToMove) stringBuilder.Append($"{(board.plyCount+1)/2}..."); 

        List<Chess.Move> line = new();
        // Is while (true) terrible style???
        while (true) {
            zobristKey = board.ZobristKey ^ ((ulong)board.plyCount << 1);
            if (!EvaluationTable.TryGetValue(zobristKey, out var computation)) break;
            if (computation.BestMove.IsNull) break;

            if(board.plyCount%2==0) stringBuilder.Append($"{board.plyCount/2}.");

            var move = new Chess.Move(computation.BestMove.RawValue);

            stringBuilder.Append(
                $"{MoveUtility.GetMoveNameSAN(move, board)} "
            );

            board.MakeMove(move, inSearch: true);

            // save moves in reverse order
            line.Insert(0, move);
        }

        // now undo all the moves
        foreach(var move in line) {
            board.UndoMove(move, inSearch: true);
        }

        return stringBuilder;
    }

    public static String EvaluationToString(float evaluation, int plyCount) {
        if (Math.Abs(evaluation) < 1e6f) return $"{evaluation:0.00}";

        int checkMatePlies = (int)(1000 - Math.Abs(evaluation)/1e6f);
        int checkMateSign = (evaluation>0 ? 1 : -1);
        return $"#{(checkMatePlies-plyCount+1)/2 * checkMateSign}";
    }
}
