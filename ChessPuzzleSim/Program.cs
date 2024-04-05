﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

/* 

dpenner1's Chess Domination Solver
=============================
Though it's been generalized, the core algorithm was coded to solve https://puzzling.stackexchange.com/q/2872/3936
Don't use this for N-Queens or anything like that. 

It's an iterative search with optional filters over a set of given major pieces. Then if feasible, targeted pawn placement.
Assuming correct code, ALL solutions not filtered out will be found given the initial starting set (no filters means a completely exhaustive search).
Note: for now, it just outputs the placement of the initial starting set - you'll have to reverse engineer the pawn placement.

Algorithmic complexity for m = number of major pieces, p = number of pawns, n = number of squares: 

  O( n^m * np^2  +  n^m * sqrt(n)*m*p  +  n^m * log(p)*p^3 )   - I believe the first two terms are tight bounds, but better math could bring the last down

For m & p on order of O(1): 
  
  O( n^(m+1) )

For m & p on order of O(sqrt(n)):  

  O( n^(sqrt(n)+2) )

For m & p on order of O(n):

  O( n^(n+3) * log(n) ) = O(heat death of the universe)

Basically, this algorithm scales way better with board size (polynomial!) than number of pieces (worse than exponential!)

--------
Note: I'm coming back to this after at least five years. Random updates:

- From what I recall, I had effectively completed the use case for solving the StackExchange question, but wanted to improve and generalize for fun.
  - There's a configuration to allow a piece to cover its own square. From what I recall, this option is not at all well suited to the algorithm and is not performant.
  - The search set-up strings contain a |0-64| entry: This was intended for parallelization. While not yet fully implemented, it should allow for partitioning the work.
    (though parallelization is currently possible across distinct major piece sets, just not within the selected major piece set yet) 
  - Originally I had to manually specify the major pieces to be used for a run. Looks like I coded automatic generation of that, but can't remember having used it

- Looking through my old comments on algorithmic complexity, I had to correct several mistakes, so maybe there are more! 
    - Most importantly, missed that initializing the chess board was O(n) instead of O(p)! 
       - Luckily there's only one place where this meaningfully contributes to higher complexity, and it might be possible to remove
    - Making known improvements should result in the third term in that Big-O analysis being dwarfed by the other two, at least in practice

- Copied the code into a .NET 7 solution, I believe I was previously using MonoDevelop .NET 4.x (Linux)

- Next things to do:
  - Get that parallelization working
  - Get the solution printout to include pawn placements
  - See about improving the pawn placement algo (including that O(n) board copy) 
      - though this might be premature optimization, in practice only about 10-15% of board evaluations have pawns
  - See about a "bishops on opposite colors" restriction
  - See about adding opposing pieces (pawns target in the other direction)
  - Search resumption is not yet smooth (especially stats & found solutions)
  - Better symmetry detection, right now it just fixes one piece to the left half of the board to remove some symmetrical solutions
     - This just plain works though until you get to allowing 3 copies of a major piece. Then you could fix eg. 2 pieces to left half, but testing
       the eight bishops revealed bugginess here, probably the NextBoard iteration messes something up here
  - The algo aborts on pawn placement impossibility, but could we possibly abort even earlier, on major piece impossibility too?

*/



// Yes, some code is ugly because I needed to code it in a performant manner. Other code is ugly because I couldn't be bothered.

// todo - check for places where unsafe code would really help
namespace dpenner1.Chess.ChessDominationSolver 
{
    class MainClass
    {
        const int ConsoleOutputLevel = 2;  // 0 = none, 1 = solutions and finals only, 2 = all (+interim reports)
        static readonly string OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChessPuzzleSim");

        // Simulation String format: <Starting board>|<Settings>|<partition>|<stats>  (spaces are OK)
        // Board string format: Standard chess notation for the pieces, separated by semicolon. Do not use pawns.
        //                      Ensure Q, R, B, N are in that order as search resumption relies on standard order. For duplicate pieces, the first listed should occupy a lower position than the second.
        // Settings: Piece set, board dims, target score, "A" for piece not attacking own square, "V" for score by piece point value, <Optimization parameters>
        // Optimization parameters: String of optimizations included. Q = disallow Queen on edge, B = disallow Bishop on edge, R = disallow rook in same rank or file as Queen or other Rook.
        //                          Use a number at the end to specify the maximum number of non-Rook / non-pawn pieces to allow on the edge of the board.
        // Stats: 4 comma separated digits representing: major boards evaluated, all boards evaluated, solutions found, elapsed seconds.

        const string allMajors = "|QRRBBNN,8x8,33,A,V,R2|0-64|0,0,0,0";
        const string missingRook = "|QRBBNN,8x8,33,A,V,|0-64|0,0,0,0";

        const string missingBothRooks = "|QBBNN,8x8,33,A,V,|0-64|0,0,0,0";
        const string missingBothBishops = "|QRRNN,8x8,33,A,V,|0-64|0,0,0,0";
        const string missingBothKnights = "|QRRBB,8x8,33,A,V,|0-64|0,0,0,0";

        const string astroTrain = "Qb6;Rh1;Ra2;Be6;Be7;Ne5|QRRBBN,8x8,33,A,V,|0-64|0,0,0,0";
        const string fiveBishops = "|BBBBB,8x8,28,A,V,|0-64|0,0,0,0";
        const string eightBishops = "|BBBBBBBB,8x8,28,A,V,2|0-64|0,0,0,0";
        const string nineBishops = "|BBBBBBBBB,8x8,28,A,V,2|0-64|0,0,0,0";
        const string eightBishopsOneRook = "|RBBBBBBBB,8x8,29,A,V,2|0-64|0,0,0,0";
        const string sixBishopsOneRook = "|RBBBBBB,8x8,28,A,V,2|0-64|0,0,0,0";

        const string sixBishopsOneKnight = "|NBBBBBB,8x8,28,A,V,2|0-64|0,0,0,0";
        const string sixBishopsTwoKnights = "|NNBBBBBB,8x8,28,A,V,2|0-64|0,0,0,0";
        const string sevenBishopsOneKnight = "|NBBBBBBB,8x8,28,A,V,2|0-64|0,0,0,0";
        const string sevenBishopsTwoKnights = "|NNBBBBBBB,8x8,28,A,V,2|0-64|0,0,0,0";

        const string fiveBishopsOneQueen = "|QBBBBB,8x8,28,A,V,2|0-64|0,0,0,0";
        const string fourBishopsOneRookOneQueen = "|QRBBBB,8x8,28,A,V,R2|0-64|0,0,0,0";
        const string twoBishopsTwoKnights = "|BBNN,8x8,28,A,V,|0-64|0,0,0,0";
        //const string threeBishopsThreeKnights = "Nf1;Ne1;Nd1;Bc1;Bb1;Ba1,Nf1;Ne1;Nd1;Bc1;Bb1;Ba1,28,,0,0,0,0";

        public static string GetTypeSetString(int numQ, int numR, int numB, int numN)
        {
            string retval = "";
            for (int i = 0; i < numQ; i++) retval += "Q";
            for (int i = 0; i < numR; i++) retval += "R";
            for (int i = 0; i < numB; i++) retval += "B";
            for (int i = 0; i < numN; i++) retval += "N";
            return retval;
        }

        public static List<string> GetTypeSets(int minPoints, int maxPoints, byte maxQ, byte maxR, byte maxB, byte maxN)
        {
            var retval = new List<string>();

            for (int numQ = 0; numQ <= maxQ; numQ++)
            {
                for (int numR = 0; numR <= maxR; numR++)
                {
                    for (int numB = 0; numB <= maxB; numB++)
                    {
                        for (int numN = 0; numN <= maxN; numN++)
                        {
                            var points = numQ * 9 + numR * 5 + numB * 3 + numN * 3;
                            if (points >= minPoints & points <= maxPoints)
                            {
                                retval.Add(GetTypeSetString(numQ, numR, numB, numN));
                            }
                        }
                    }
                }
            }

            return retval;
        }

        public static void Main(string[] args)
        {
            Directory.CreateDirectory(OutputDirectory);
            RunSearch(eightBishops, OutputDirectory);
        
            /* Run searches in parallel

            // this is to get all major piece sets scoring between 23-33 with max one queen, max two of the other major piece sets
            var searches = GetTypeSets(23, 33, 1, 2, 2, 2);

            var searchesToRun = new List<string>
            {

            };
            Parallel.ForEach(searchesToRun, s => RunSearch(s, OutputDirectory));

            */
        }

        public static void RunSearch(string searchStartString, string outFileDir)
        {
            var info = new SearchInfo(searchStartString);

            var board = string.IsNullOrEmpty(info.CurrentBoardRepresentation) ? new Board(info.Settings.TypeSet, info.Settings) : Board.FromRepresentation(info.CurrentBoardRepresentation);

            long updateFrequencyMinutes = 1;
            long lastUpdateTimerMs = 0;

            var timer = new Stopwatch();
            timer.Start();
            
            // number of possible of major piece boards is O(n^m), so loop is O(n^m) * O( np^2 + sqrt(n)*m*p + log(p)*p^3 )
            // = O( n^m * np^2  +  n^m * sqrt(n)*m*p  +  n^m * log(p)*p^3 ) in worst case
            // Obviously there's optimizations everywhere to try to avoid the worst cases
            long timeSinceLastUpdateMs = 0;
            do
            {
                info.Stats.EvaluatedMajorBoardsCount++;   
                
                // O( np^2 + sqrt(n)*(m+p)*p + log(p)*p^3 )
                if (board.EvaluateAllPawnPossibilities(info))
                {
                    // only outputs the major pieces, the pawns were a bit of an after-thought
                    info.Stats.SolutionsCount++;
                    var s = "SOLUTION FOUND\n";
                    s += "--------------\n";
                    s += board.ToString();
                    Output(s, outFileDir, info, ConsoleOutputLevel >= 1);
                }

                timeSinceLastUpdateMs = timer.ElapsedMilliseconds - lastUpdateTimerMs;
                if (timeSinceLastUpdateMs > updateFrequencyMinutes * 60000L)
                {
                    lastUpdateTimerMs = timer.ElapsedMilliseconds;
                    info.Stats.TotalMilliSeconds += timeSinceLastUpdateMs;
                    info.CurrentBoardRepresentation = board.GetRepresentation();
                    Output(info.GetInterimReport(), outFileDir, info, ConsoleOutputLevel >= 2);
                }

            } while (board.NextBoard(info.Settings));
            // TODO - haven't done the algo complexity analysis on the NextBoard function, but im guessing it gets dwarfed by EvaluateAllPawnPossiblities anyways

            info.Stats.TotalMilliSeconds += timeSinceLastUpdateMs;
            Output(info.GetFinalReport(), outFileDir, info, ConsoleOutputLevel >= 1);
        }

        private static void Output(string s, string outFileDir, SearchInfo info, bool alsoOutputToConsole)
        {      
            // file name basically everything needed to ensure search paramaters the same
            string fileName = string.Join(",", info.Settings.GetValues()) + "-" + string.Join(",", info.Settings.LastPieceStartPosition, info.Settings.LastPieceEndPosition);

            using (var sw = new StreamWriter(Path.Combine(outFileDir, fileName), append: true))
            {
                sw.WriteLine(s);
                sw.WriteLine();
            }

            if (alsoOutputToConsole)
            {
                Console.WriteLine(s);
                Console.WriteLine();
            }
        }
    }
}