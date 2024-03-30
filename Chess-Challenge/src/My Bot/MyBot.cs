using ChessChallenge.API;
using Microsoft.CodeAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Authentication.ExtendedProtection;
using System.Threading.Tasks.Dataflow;
using System.Xml;

public class MyBot : IChessBot
{
    static int ExplorationDepth = 3;
    static int ExtraCaptureDepth = 0;
    static float CheckMateValue = 1e6f; // Large, but finite value for checkmate.
    static float[,,] WhitePieceValues;
    static Dictionary<ulong, Valuation> PreviousEvaluations; // sends (board Zobrist key) => (depth, evaluation containing eval, depth, line of best moves)
    
    static int BoardCounter = 0;
    static int RunningAverage_BoardEvaluations = 0;

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

        PreviousEvaluations = new();
    }

    public Move Think(Board board, Timer timer)
    {
        // adapt depth based on time remaining
        // DepthDecider(board, timer);

        BoardCounter = 0;

        // On the top level we still check the table, mainly to save the top level computations.
        Valuation valuation = EvaluateCheckTable(
                                    board, 
                                    ExplorationDepth,
                                    float.NegativeInfinity,
                                    float.PositiveInfinity
                                    );
        
        // Console.WriteLine(timer.MillisecondsElapsedThisTurn);
        RunningAverage_BoardEvaluations = (9*RunningAverage_BoardEvaluations + BoardCounter)/10;

        // Print valuation and number of boards evaluated
        Console.WriteLine(valuation.ToString() + " (" +  BoardCounter.ToString("0.0e+0") + ", "
                                     + RunningAverage_BoardEvaluations.ToString("0.0e+0") + ")");

        return valuation.bestMove();
    }

    private static int MoveEvaluationOrder(Board board, Move move){
        board.MakeMove(move);

        var order = -board.GetLegalMoves().Length;
        order += board.IsInCheckmate() ? 1000 : 0;
        order += board.IsInCheck() ? 500 : 0;
        order += move.IsCapture ? 250 : 0;

        // order += (int)( 100 * EvaluateBoard(board));

        board.UndoMove(move);
        return order;
    }

    // EvaluateRecursively searches for the best move on board up to depth, discarding any moves that are below minEval or above maxEval
    public static Valuation EvaluateRecursively(Board board, int depth, 
                                            float lowerCutoff, float upperCutoff){
        // Deal with trivial cases first: checkmate, draw
        if (board.IsInCheckmate()) return new Valuation(-CheckMateValue*(10+depth), 100);
        if (board.IsDraw()) return new Valuation(0, 100);

        // Check if we need to stop the search for depth reasons
        if (depth == -ExtraCaptureDepth)  return new Valuation(EvaluateBoard(board), depth); 
        var moves = board.GetLegalMoves(depth<=0); // if depth is <= 0, get captures only
        if (!moves.Any()) return new Valuation(EvaluateBoard(board), depth);

        // Sort moves (if remaining depth >= 2)
        var sortedMoves = (depth >= 2) ? moves.OrderByDescending(move => MoveEvaluationOrder(board, move)) 
                                        : moves.AsEnumerable();

        float bestEval = float.NegativeInfinity;
        Valuation bestValuation = default; // new Valuation(0, 0);
        // Loop through moves, recursively calling this function and employing alpha-beta pruning
        foreach(var move in sortedMoves){
            board.MakeMove(move);
            var valuation = EvaluateCheckTable(
                                    board, 
                                    depth-1,
                                    -upperCutoff,
                                    -lowerCutoff
                                    );
            valuation = valuation.extend(move, depth);
            board.UndoMove(move);
            if(valuation.eval >= bestEval) {
                lowerCutoff = Math.Max(lowerCutoff, valuation.eval);
                bestEval = valuation.eval;
                bestValuation = valuation;
                if(lowerCutoff > upperCutoff) {
                    // too good to be true. this should not be saved in the PreviousEvaluations
                    break;
                }
            }
        }

        // if (depth >= 2) { Console.WriteLine(bestValuation.myString()); }
        return bestValuation;
    }

    public static Valuation EvaluateCheckTable(Board board, int depth, 
                                            float lowerCutoff, float upperCutoff){

        // First, see if we evaluated this position already at sufficient depth
        // XOR with plycount to account for repetitions
        var zobristKey = board.ZobristKey ^ ((ulong)(board.PlyCount) << 1);
        // Every evaluation is stored from white's perspective

        Boolean keyExisted = false;
        if(PreviousEvaluations.TryGetValue(zobristKey, out var previousEval)){
            // we have seen this position before - check depth
            if(previousEval.depth >= depth){
                return previousEval;
            }
            keyExisted = true;
            // else carry on - the position needs to be reevaluated
        } 

        var valuation = EvaluateRecursively(board, depth, lowerCutoff, upperCutoff);
        if (lowerCutoff <= valuation.eval && valuation.eval <= upperCutoff) {
            // This evaluation was not cutoff and can be trusted, so we save it.
            if (!keyExisted) PreviousEvaluations.Add(zobristKey, default);
            PreviousEvaluations[zobristKey] = valuation;
        }
        return valuation;
    }

    private static float EvaluateBoard(Board board) {
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
        BoardCounter ++;
        return totalEvaluation;
    }

    private void DepthDecider(Board board, Timer timer){
        if(timer.MillisecondsRemaining > 10_000) {
            if(board.PlyCount < 10){
                ExplorationDepth = 3;
                ExtraCaptureDepth = 4;
            } else {
                ExplorationDepth = 4;
                ExtraCaptureDepth = 2;
            }
        }else{
            ExplorationDepth = 3;
            ExtraCaptureDepth = 1;
            if(timer.MillisecondsRemaining < 1_000) {
                ExtraCaptureDepth = 0;
            }
            else if (timer.MillisecondsRemaining < 100) {
                ExplorationDepth = 2;
            }
        }
        return;
    }
}


public struct Valuation {

    public Valuation(float e, int d){ // should also pass depth?
        eval = e;
        depth = Math.Min(d,0);
        extraDepth = Math.Max(-d,0);
        line = new List<Move>();
    }

    public override String ToString(){
        var output = eval.ToString("0.00") + " (" + depth.ToString() + "+" + extraDepth.ToString() + ") ";
        for (int i = line.Count-1; i >= 0; i--){
            output += " " + line[i].ToString() + ",";
        }
        return output;
    }

    private List<Move> line;
    public float eval {get; set;}
    public int depth {get; set; }
    public int extraDepth {get; set; }
    // private Board board { get; init; }

    public Move bestMove() {
        if (line.Any()) return line.Last();
        else return Move.NullMove;
    }

    public Valuation extend(Move move, int currentDepth) {
        var extendedEval = new Valuation(-eval, currentDepth);
        extendedEval.extraDepth = extraDepth;
        extendedEval.line = line.ToList(); //copy line
        extendedEval.line.Add(move); //add new move
        extendedEval.depth = currentDepth;
        return extendedEval;
    }
}