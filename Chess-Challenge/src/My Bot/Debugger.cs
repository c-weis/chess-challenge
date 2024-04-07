using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ChessChallenge.Chess;

namespace ChessChallenge.Debugging;
static class Debugger{
    public static void OutputLatestComputationStats(MyBot bot, Chess.Board board){
        if (false) Debug.WriteLine($"{bot.LastComputation.Summary(board)}| {bot.BoardEvaluationCounter:0}");
    }    

    public static void OutputSummary(Computation computation, API.Board board){
        Debug.WriteLine($"{computation.Summary(board)}");
    }

    public static StringBuilder Summary(this Computation computation, API.Board board) {
        return computation.Summary(new Chess.Board(board.SecretInternalBoard));
    }

    public static StringBuilder Summary(this Computation computation, Chess.Board board) {
        StringBuilder stringBuilder = new(
            $"{computation.Evaluation:0.00} ({computation.Depth}+{computation.ExtraDepth}) "
        );

        // convert API type moves to equivalent Chess.Move member and keep track of ply count
        // reverse order because the moves are stored in reverse in Computation.Line
        List<(int, Chess.Move)> enumeratedValuationMoves = 
            Enumerable
                .Reverse(computation.Line)
                .Where(move => !move.IsNull)
                .Select(
                    (apiMove, index) => (board.plyCount + index + 1, new Chess.Move(apiMove.RawValue))
        ).ToList();

        // iterate through moves in line and append their SAN name decorated with move count
        // special case: if the bot is black, start printing line with X...
        if (!board.IsWhiteToMove) stringBuilder.Append($"{(board.plyCount+1)/2}..."); 
        foreach (var (plyNumber, move) in enumeratedValuationMoves){
            if(plyNumber%2==1) stringBuilder.Append($"{(plyNumber+1)/2}.");

            stringBuilder.Append(
                $"{MoveUtility.GetMoveNameSAN(move, board)} "
            );

            board.MakeMove(move, inSearch: true);
        }

        // now undo all the moves
        foreach (var (_, move) in Enumerable.Reverse(enumeratedValuationMoves)){
            board.UndoMove(move, inSearch: true);
        }

        return stringBuilder;
    }
}