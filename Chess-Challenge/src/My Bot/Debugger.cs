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
        var startingEval = EvaluationTable[zobristKey];
        StringBuilder stringBuilder = new(
            $"{startingEval.Evaluation:0.00} ({startingEval.Depth}) "
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


//         // convert API type moves to equivalent Chess.Move member and keep track of ply count
//         // reverse order because the moves are stored in reverse in Computation.Line
//         List<(int, Chess.Move)> enumeratedValuationMoves = 
//             Enumerable
//                 .Reverse(computation.Line)
//                 .Where(move => !move.IsNull)
//                 .Select(
//                     (apiMove, index) => (board.plyCount + index + 1, new Chess.Move(apiMove.RawValue))
//         ).ToList();
// 
//         // iterate through moves in line and append their SAN name decorated with move count
//         // special case: if the bot is black, start printing line with X...
//         if (!board.IsWhiteToMove) stringBuilder.Append($"{(board.plyCount+1)/2}..."); 
//         foreach (var (plyNumber, move) in enumeratedValuationMoves){
//             if(plyNumber%2==1) stringBuilder.Append($"{(plyNumber+1)/2}.");
// 
//             stringBuilder.Append(
//                 $"{MoveUtility.GetMoveNameSAN(move, board)} "
//             );
// 
//             board.MakeMove(move, inSearch: true);
//         }
// 
//         // now undo all the moves
//         foreach (var (_, move) in Enumerable.Reverse(enumeratedValuationMoves)){
//             board.UndoMove(move, inSearch: true);
//         }

        return stringBuilder;
    }
}
