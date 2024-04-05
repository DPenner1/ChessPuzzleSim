using System;
using System.Linq; // only used in infrequently called methods (avoid performance hits)
using System.Collections.Generic;

namespace dpenner1.Chess.ChessDominationSolver
{
    // Algorithmic complexities: m = number of major pieces p = number of pawns, n = number of squares on board... assume RankDim = FileDim
    public class Board
    {
        // somewhat arbitrary, I just think this might work better for piece rules placement, mainly I just want consistent ordering when possible
        // and when there's only 1 Q, I want that as the last piece
        private static readonly List<char> PieceOrderPreference = new List<char> { '^', 'R', 'B', 'N', 'K', 'Q' };

        const int RankDim = SearchSettings.RankDim;
        const int FileDim = SearchSettings.FileDim;
        const int NumSquares = RankDim * FileDim;
        const int MaxPawns = FileDim;
         
        // array of pieces instead of board array to avoid iterating over 64 space board, iterate over much smaller list when possible
        // pieces should be ordered by most frequent mover to least frequent.
        // with like pieces, the higher positioned piece is first and most frequently moving
        Piece[] pieces;

        // turns out a board array of sorts is still needed at times, try to index only, not iterate
        // i think the only time it's somewhat iterated is when looking for available spots, which would be O(p) anyways
        bool[,] pieceLocations = new bool[RankDim, FileDim];
        // TODO: but of course initialization of default false values is O(n)!!!!
        // It's only used in a loop in one location (copying the board)... when I thought this was O(p) in a loop that already had an O(p)
        // action, this seemed acceptable to me, but O(n) isn't great. It might be possible to modify the board in O(p) instead of O(n) copy.

        // wow, a third data structure for piece info!
        // mostly needed for rook optimization, need to know where the queen/other rooks are, but also occasionally how many of each type
        // the most frequently moving should be listed first for consistency
        Dictionary<char, List<Piece>> typeDict = new Dictionary<char, List<Piece>>();

        private int totalPoints = 0;
        private int numNonRookPawnOnEdge = 0;

        private int numPiecesToRestrictToLeftHalf = 0;

        // todo - lastpiecestart (the parallelization option)
        public Board(string pieceSet, SearchSettings settings)
        {
            Dictionary<char, int> typeCounts = new Dictionary<char, int>();

            foreach (var type in PieceOrderPreference)
            {
                typeCounts.Add(type, 0);
            }

            foreach (var p in pieceSet)
            {
                typeCounts[p] = typeCounts[p] + 1;
            }

            // we need to determine last piece type to use for symmetry (basically halving search time)
            // obviously if there's a type with only 1 occurrence, that has to be used,
            // but some quick checks (nothing formal) lead me to believe in the case no piece has just 1 occurrence,
            // it's still most efficient to take the type that has the least occurrences
            int minCount = typeCounts.Values.Where(x => x != 0).Min();

            char lastPieceType = ' ';
            foreach (var type in PieceOrderPreference)
            {
                if (typeCounts[type] == minCount)
                {
                    lastPieceType = type;
                    break;
                }
            }

            pieces = new Piece[typeCounts.Values.Sum()];
            int pIndex = 0;

            foreach (var type in PieceOrderPreference)
            {
                if (type != lastPieceType)
                {
                    for (int i = 0; i < typeCounts[type]; i++)
                    {
                        AddPieceTrackingInfo(new Piece(type, 0, 0), pIndex);
                        pIndex++;
                    }
                }
            }

            for (int i = 0; i < minCount; i++)
            {
                AddPieceTrackingInfo(new Piece(lastPieceType, 0, 0), pIndex);
                pIndex++;
            }

            // I think this works for compensating for symmetry (comment out as it might not actually work)
            //numPiecesToRestrictToLeftHalf = (minCount - 1) / 2 + 1;

            numPiecesToRestrictToLeftHalf = 1;

            var lastPiece = pieces[pieces.Length - 1];
            lastPiece.File = settings.LastPieceStartPosition % FileDim;
            lastPiece.Rank = settings.LastPieceStartPosition * FileDim;
            AddToBoard(lastPiece);

            if (!PlacePiecesInLowestPossiblePositions(pieces.Length - 2, settings))
            {
                throw new InvalidOperationException("Could not initiate legal board with given pieces and settings");
            }
        }

        // TODO - i thought this was O(p), but the pieceLocations field array initialization would be O(n)!
        public Board(Piece[] pieceSet, Piece? extra)
        {
            this.pieces = new Piece[pieceSet.Length + (extra == null ? 0 : 1)];

            for (int i = 0; i < pieceSet.Length; i++)
            {
                var p = new Piece(pieceSet[i]);
                AddPieceTrackingInfo(p, i);
                AddToBoard(p);
            }

            if (extra != null)
            {
                var p = new Piece(extra);
                AddPieceTrackingInfo(p, pieceSet.Length);
                AddToBoard(p);
            }

        }

        // Pieces are placed in reverse order!
        private bool PlacePiecesInLowestPossiblePositions(int startIndex, SearchSettings settings)
        {
            for (int i = startIndex; i >= 0; i--)
            {
                if (!PlaceSinglePieceInLowestPossiblePosition(i, settings)) return false;
            }

            return true;
        }

        private bool PlaceSinglePieceInLowestPossiblePosition(int pIndex, SearchSettings settings)
        {
            // algo: basically place at 0 or at piece of same type, then start move it to the next legal position

            var pieceToPlace = pieces[pIndex];
            if (pIndex < pieces.Length - 1 && pieces[pIndex + 1].Type == pieceToPlace.Type)
            {
                // must place after piece of same type
                pieceToPlace.File = pieces[pIndex + 1].File;
                pieceToPlace.Rank = pieces[pIndex + 1].Rank;
            }
            else
            {
                pieceToPlace.File = 0;
                pieceToPlace.Rank = 0;
            }

            // special case for 0 where we don't move the piece
            if (pieceToPlace.Position == 0 && !pieceLocations[0, 0]) // technically checked in the invalid, but expected to be common, so save time by checking up front
            {
                bool invalidRank = false;
                bool invalidFile = false;
                IsInvalidRankThenFileToPlacePiece(0, 0, settings, out invalidRank, out invalidFile);

                if (!invalidRank && !invalidFile)
                {
                    AddToBoard(pieces[pIndex]);
                    return true;
                }
            }

            return MoveSinglePiece(pIndex, settings);
        }

        private void AddToBoard(Piece p)
        {
            if (!p.OnBoard)
            {
                pieceLocations[p.Rank, p.File] = true;
                p.OnBoard = true;
                if (p.IsOnEdge && p.Type != 'R' && p.Type != '^') numNonRookPawnOnEdge++;
            }

        }

        private void RemoveFromBoard(Piece p)
        {
            if (p.OnBoard)
            {
                if (p.IsOnEdge && p.Type != 'R' && p.Type != '^') numNonRookPawnOnEdge--;
                pieceLocations[p.Rank, p.File] = false;
                p.OnBoard = false;
            }
        }

        public static Board FromRepresentation(string representation)
        {
            var parts = representation.Trim().Split(';');
            var pieces = new Piece[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                pieces[i] = new Piece(parts[i]);
            }

            return new Board(pieces, null);
        }

        private void AddPieceTrackingInfo(Piece p, int i)
        {
            pieces[i] = p;
            if (!typeDict.ContainsKey(p.Type)) typeDict.Add(p.Type, new List<Piece>());
            typeDict[p.Type].Add(p);
            totalPoints += p.Points;
        }
       
        public bool NextBoard(SearchSettings settings)
        {
            // so i had decided to do manual iteration for the combinatorics of the like pieces on the board...
            // ...it was probably a bad idea, but at least now I can fine-tune the performance.

            int pIndex = 0;

            int newIndex;
            do
            {
                pIndex = MovePieces(pIndex, settings);
                if (pIndex >= pieces.Length) return false;

                newIndex = pIndex;
                newIndex = ReplaceOffBoardPieces(pIndex, settings);
                if (newIndex >= pieces.Length) return false;
            } while (newIndex != pIndex);

            return true;
        }

        private int MovePieces(int pIndex, SearchSettings settings)
        {
            if (pIndex >= pieces.Length) return pIndex;

            var pieceToMove = pieces[pIndex];
            RemoveFromBoard(pieceToMove);

            // note the logic is that once a piece is chosen for movement, it is the fastest remaining piece on the board
            // it therefore has freedom of movement to move to the next available spot regardless of other pieces
            // this also means the piece can skip available spaces for optimization purposes

            if (pieceToMove.Position == NumSquares - 1)
            {
                if (pIndex == pieces.Length - 1) return pIndex + 1;  // due to left-half restriction of last piece, don't think this can occur on a board bigger than 2 files
                return MovePieces(pIndex + 1, settings);
            }

            if (!MoveSinglePiece(pIndex, settings))
            {
                return MovePieces(pIndex + 1, settings);
            }

            return pIndex;
        }

        // piece should already be removed from board
        private bool MoveSinglePiece(int pIndex, SearchSettings settings)
        {
            var pieceToMove = pieces[pIndex];

            bool moveRank = false;
            bool moveFile = true;
            bool isLastPiece = pIndex == pieces.Length - 1;

            do
            {
                if (isLastPiece && pieceToMove.Position >= settings.LastPieceEndPosition) return false;

                if (moveRank)
                {
                    if (pieceToMove.Rank == RankDim - 1) return false;

                    pieceToMove.MoveToNextRank();
                }
                else // moveFile = true, we wouldn't still be in the loop if otherwise
                {
                    if (IsLeftHalfPieceAtCentre(pIndex, pieceToMove.File)) // last piece stays on left half of board
                    {
                        if (pieceToMove.Rank == RankDim - 1) return false;
                        pieceToMove.MoveToNextRank(); // note that when moving the last piece, it's the only one left on the board
                    }
                    else
                    {
                        if (pieceToMove.Position == NumSquares - 1) return false;
                        pieceToMove.MoveToNextPosition();
                    }
                }

                IsInvalidRankThenFileToPlacePiece(pieceToMove.Rank, pieceToMove.File, settings, out moveRank, out moveFile);

            } while (moveRank || moveFile);

            AddToBoard(pieceToMove);
            return true;
        }

        private int ReplaceOffBoardPieces(int pIndex, SearchSettings settings)
        {
            int numSameType = 0;
            for (int i = pIndex - 1; i >= 0; i--)
            {
                if (pieces[i].Type == pieces[pIndex].Type) numSameType++;
                else break;
            }

            // place pieces of same type after the like piece remaining on board (if any)
            // then place the rest
            if (!PlacePiecesInLowestPossiblePositions(pIndex - 1, settings))
            { 
                //placement failed, we'll have to try the next piece later
                for (int i = 0; i <= pIndex; i++)
                {
                    RemoveFromBoard(pieces[i]);
                }
                return pIndex + 1;
            }

            return pIndex;
        }

        private bool IsLeftHalfPieceAtCentre(int pIndex, int file)
        {
            return pIndex >= pieces.Length - numPiecesToRestrictToLeftHalf && file >= ((FileDim + 1) / 2 - 1);
        }

        // checks rank first since that allows skipping
        private void IsInvalidRankThenFileToPlacePiece(int rank, int file, SearchSettings settings, out bool invalidRank, out bool invalidFile)
        {
            invalidRank = false;
            invalidFile = false;

            // Check in valid rank
            if (!settings.AllowRooksQueensOnSameRanksFiles) 
            {
                foreach (var rook in typeDict['R'])
                {
                    if (rook.OnBoard)
                    {
                        if (rank == rook.Rank)
                        {
                            invalidRank = true;
                            return;
                        }
                        if (file == rook.File)
                        {
                            invalidFile = true; // save for later so we don't re-iterate
                        }
                    }
                }

                foreach (var queen in typeDict['Q'])
                {
                    if (queen.OnBoard)
                    {
                        if (rank == queen.Rank)
                        {
                            invalidRank = true;
                            return;
                        }
                        if (file == queen.File)
                        {
                            invalidFile = true; // save for later so we don't re-iterate
                        }
                    }
                }
            }
            if (numNonRookPawnOnEdge >= settings.MaxNonRookNonPawnOnEdge)
            {
                if (rank == 0 || rank == RankDim - 1)
                {
                    invalidRank = true;
                    return;
                }
                if (file == 0 || file == FileDim - 1)
                {
                    invalidFile = true;
                }
            }

            // Rank is valid, check specific file
            if (invalidFile) return;

            if (pieceLocations[rank, file])
            {
                invalidFile = true;
                return;
            }
        }

        private bool IsRookOrQueenInSameRank(Piece p)
        {
            foreach (var rook in typeDict['R'])
            {
                if (rook.OnBoard && rook != p && p.Rank == rook.Rank) return true;
            }

            foreach (var queen in typeDict['Q'])
            {
                if (queen.OnBoard && queen != p && p.Rank == queen.Rank) return true;
            }

            return false;
        }

        private bool IsRookOrQueenInSameFile(Piece p)
        {
            foreach (var rook in typeDict['R'])
            {
                if (rook.OnBoard && rook != p && p.File == rook.File) return true;
            }

            foreach (var queen in typeDict['Q'])
            {
                if (queen.OnBoard && queen != p && p.File == queen.File) return true;
            }

            return false;
        }

        public bool EvaluateAllPawnPossibilities(SearchInfo info)
        {
            var tracker = new PawnConfigurationTracker();  // single tracker so that the recursive descent does not evaluate already checked configurations
            return EvaluateAllPawnPossibilities(tracker, info);
        }

        // probably O( np + sqrt(n)*(m+p)*p + log(p)*p^3 )
        private bool EvaluateAllPawnPossibilities(PawnConfigurationTracker attemptedPawnConfigurations, SearchInfo info)
        {
            var possiblePositions = new HashSet<int>();
            int currentPawnCount = 0;
            if (typeDict.ContainsKey('^')) currentPawnCount = typeDict['^'].Count;

            int scoreMetric = info.Settings.ScoreByPieceValue ? totalPoints : pieces.Length;
            int availablePawns = Math.Min(info.Settings.TargetScore - scoreMetric, MaxPawns - currentPawnCount);

            if (availablePawns < 0) return false;  // allow zero, since we still need to check if adding no pawns (eg current board) attacks all squares

            if (ArePawnsPossible(availablePawns, possiblePositions, info.Settings, info.Stats)) // // O(n + m*sqrt(n))
            {
                // basically, if it's possible to place pawns yet no positions are given, that's because none are needed (solution found)
                if (possiblePositions.Count == 0) return true;

                // add a pawn in each position and recursively evaluate (unfortunately)
                // there was an async version for this loop, though with 33 target score, in practice only 10-15% of boards would benefit, 
                // not worth maintaining and risking sync issue, use your parallelism budget on just running multiple different searches
                foreach (var position in possiblePositions) // may loop O(p) times - possiblePositions.Count is max 3*numPawns
                {
                    var configurationToAttempt = new int[currentPawnCount + 1];
                    for (int i = 0; i < currentPawnCount; i++)
                    {
                        configurationToAttempt[i] = typeDict['^'][i].Position;
                    }
                    configurationToAttempt[configurationToAttempt.Length - 1] = position;
                    Array.Sort(configurationToAttempt);  // O(p*log(p)) - this also means we try lower positions first, which are usually harder to cover

                    if (attemptedPawnConfigurations.TryAddPawnConfiguration(configurationToAttempt)) // O(p)
                    {
                        var pawn = new Piece('^', position / FileDim, position % FileDim);
                        // TODO: surely there's a better algorithm than adding one at a time! 
                        // At minimum, all previously uncovered squares should be simultaneously covered, then a recursive evaluation

                        var newPawnBoard = new Board(this.pieces, pawn); // create new board to avoid modification to old
                        // TODO: I initially thought this was O(p), but its O(n) to create a new board!

                        if (newPawnBoard.EvaluateAllPawnPossibilities(attemptedPawnConfigurations, info)) return true;
                        // max recursion depth is O(p) since we add 1 pawn at a time...
                        // I've not properly learned to calculate big-O of recursive functions, but I think I have to multiply O(p) by what has happened so far
                        // so O(p) * [ O(n + m*sqrt(n) + log(p)*p^2 + np) ]  (can drop lower order n term from ArePawnsPossible)
                        // in the end we have O( np^2 + sqrt(n)*m*p + log(p)*p^3 )
                        // (though i feel like more rigorous math could bring the upper bound of that last term down)
                    }
                }
            }

            return false;
        }

        // returns true if pawns are feasible, with possible pawn locations filled in the possiblePositions parameter
        // Note: we allow pawns being possible with 0 available since this method simultaneously checks attacked locations, so a solution may be found
        // O(n + m*sqrt(n))
        public bool ArePawnsPossible(int availablePawns, HashSet<int> possiblePositions, SearchSettings settings, SearchStats stats)
        {
            bool[,] coveredSquares = GetCoveredSquares(settings.PieceCoversOwnSquare);   // O(n + m*sqrt(n))
            stats.EvaluatedTotalBoardsCount++;

            int minRequiredPawns = 0;

            // variables for counting spaces covered by pawns placed directly on uncovered square when PieceCoversOwnSquare is true
            int nextRankCoveredEvenSpaces = 0;
            int nextRankCoveredOddSpaces = 0;

            for (int file = 0; file < FileDim; file++)
            {
                // pawns cannot attack rank 0
                if (!coveredSquares[0, file])
                {
                    if (!settings.AllowPawnsOnStartingRank || !settings.PieceCoversOwnSquare) return false; // no way to cover rank 0

                    // TODO - with PieceCoversOwnSquare as true, this is in fact mandatory, not just possible
                    minRequiredPawns++;
                    possiblePositions.Add(file);

                    if (file % 0 == 2) nextRankCoveredEvenSpaces += 2;
                    else nextRankCoveredOddSpaces += 2;
                }
            }

            int StartRank = 1;
            if (!settings.AllowPawnsOnStartingRank)
            {
                for (int file = 0; file < FileDim; file++)
                {
                    if (!coveredSquares[1, file])
                    {
                        if (!settings.PieceCoversOwnSquare) return false; // pawns cannot attack rank 1

                        // TODO - with PieceCoversOwnSquare as true, this is in fact mandatory, not just possible
                        minRequiredPawns++; 
                        possiblePositions.Add(FileDim + file);

                        if (file % 0 == 2) nextRankCoveredEvenSpaces += 2;
                        else nextRankCoveredOddSpaces += 2;
                    }

                }
                StartRank = 2;
            }
            
            // total O(n)
            for (int rank = StartRank; rank < RankDim; rank++)
            {
                int rankStartPossiblePositionCount = possiblePositions.Count;
                int rankStartMinReqPawns = minRequiredPawns;

                //even files then odd files (pawn can attack 2 evens or 2 odds)
                int missingSpaces = 0;
                for (int file = 0; file < FileDim; file += 2)
                {
                    if (!coveredSquares[rank, file])
                    {
                        missingSpaces++;
                        if (file != 0) TryAddPosition(rank - 1, file - 1, possiblePositions);
                        if (FileDim % 2 == 0 || file != FileDim - 1) TryAddPosition(rank - 1, file + 1, possiblePositions);
                        if (settings.PieceCoversOwnSquare) possiblePositions.Add(rank * FileDim + file);
                    }
                }
                minRequiredPawns += (Math.Max(0, missingSpaces - nextRankCoveredEvenSpaces) + 1) / 2;
                if (settings.PieceCoversOwnSquare) nextRankCoveredEvenSpaces = missingSpaces * 2;  //since each missing space is a potential pawn location, covering 2 next rank

                missingSpaces = 0;
                for (int file = 1; file < FileDim; file += 2)
                {
                    if (!coveredSquares[rank, file])
                    {
                        missingSpaces++;
                        TryAddPosition(rank - 1, file - 1, possiblePositions); // file can't be 0, this is the odd loop
                        if (file != FileDim - 1) TryAddPosition(rank - 1, file + 1, possiblePositions);
                        if (settings.PieceCoversOwnSquare) possiblePositions.Add(rank * FileDim + file);
                    }
                }
                minRequiredPawns += (Math.Max(0, missingSpaces - nextRankCoveredOddSpaces) + 1) / 2;
                if (settings.PieceCoversOwnSquare) nextRankCoveredOddSpaces = missingSpaces*2;  //since each missing space is a potential pawn location, covering 2 next rank

                // need too many pawns
                if (minRequiredPawns > availablePawns) return false;

                // too many pieces are in the way of pawn placement in this rank
                if (!settings.PieceCoversOwnSquare && minRequiredPawns - rankStartMinReqPawns > possiblePositions.Count - rankStartPossiblePositionCount) return false;
            }

            return true;
        }

        // O(1)
        private void TryAddPosition(int rank, int file, HashSet<int> positions)
        {
            if (!pieceLocations[rank, file]) positions.Add(rank * FileDim + file);
        }

        // try to minimize calls here
        // O(n + m*sqrt(n))  (p term dropped as O(p) <= O(n))
        private bool[,] GetCoveredSquares(bool includeOwnSquare)
        {
            var coveredSquares = new bool[RankDim, FileDim]; // O(n)
            // I initially missed that initialization with default values is O(n) ... 
            // but this method is mainly only used in a method that already has an O(n) term, so its not too bad

            foreach (var piece in pieces) // O(m+p) ... but note pawn evals are O(1), not O(sqrt(n))! (technically so are knights)
            {
                if (includeOwnSquare) coveredSquares[piece.Rank, piece.File] = true;
                
                //O(sqrt(n))
                switch (piece.Type)
                {
                    case 'Q':
                        EvaluateRankAndFiles(piece, coveredSquares);
                        EvaluateDiagonals(piece, coveredSquares);
                        break;
                    case 'R':
                        EvaluateRankAndFiles(piece, coveredSquares);
                        break;
                    case 'B':
                        EvaluateDiagonals(piece, coveredSquares);
                        break;
                    case 'N':
                        EvaluateKnightAttacks(piece, coveredSquares);
                        break;
                    case '^':
                        EvaluatePawnAttacks(piece, coveredSquares);
                        break;

                    default: throw new InvalidOperationException("Sanity check failed");
                }
            }

            return coveredSquares;
        }

        // O(1)
        private void EvaluatePawnAttacks(Piece piece, bool[,] attackedSquares)
        {
            // assume pawn not on last rank - TODO: this is a false assumption if PieceCoversOwnSquare is true
            if (piece.File != 0) attackedSquares[piece.Rank + 1, piece.File - 1] = true;
            if (piece.File != FileDim - 1) attackedSquares[piece.Rank + 1, piece.File + 1] = true;
        }

        // O(1)
        private void EvaluateKnightAttacks(Piece piece, bool[,] attackedSquares)
        {
            EvaluateSingleKnightAttack(piece, attackedSquares, 1, 2);
            EvaluateSingleKnightAttack(piece, attackedSquares, -1, 2);
            EvaluateSingleKnightAttack(piece, attackedSquares, 1, -2);
            EvaluateSingleKnightAttack(piece, attackedSquares, -1, -2);
            EvaluateSingleKnightAttack(piece, attackedSquares, 2, 1);
            EvaluateSingleKnightAttack(piece, attackedSquares, -2, 1);
            EvaluateSingleKnightAttack(piece, attackedSquares, 2, -1);
            EvaluateSingleKnightAttack(piece, attackedSquares, -2, -1);
        }

        // O(1)
        private void EvaluateSingleKnightAttack(Piece piece, bool[,] attackedSquares, int rankOffset, int fileOffset)
        {
            int rank = piece.Rank + rankOffset;
            int file = piece.File + fileOffset;

            if (rank >= 0 && rank < RankDim && file >= 0 && file < FileDim) attackedSquares[rank, file] = true;
        }

        // O(sqrt(n))
        private void EvaluateRankAndFiles(Piece piece, bool[,] attackedSquares)
        {
            for (int rank = piece.Rank + 1; rank < RankDim; rank++)
            {
                attackedSquares[rank, piece.File] = true;
                if (pieceLocations[rank, piece.File]) break;
            }

            for (int rank = piece.Rank - 1; rank >= 0; rank--)
            {
                attackedSquares[rank, piece.File] = true;
                if (pieceLocations[rank, piece.File]) break;
            }

            for (int file = piece.File + 1; file < FileDim; file++)
            {
                attackedSquares[piece.Rank, file] = true;
                if (pieceLocations[piece.Rank, file]) break;
            }

            for (int file = piece.File - 1; file >= 0; file--)
            {
                attackedSquares[piece.Rank, file] = true;
                if (pieceLocations[piece.Rank, file]) break;
            }
        }

        // O(sqrt(n))
        private void EvaluateDiagonals(Piece piece, bool[,] attackedSquares)
        {
            EvaluateSingleDiagonal(piece, attackedSquares, 1, 1);
            EvaluateSingleDiagonal(piece, attackedSquares, -1, 1);
            EvaluateSingleDiagonal(piece, attackedSquares, 1, -1);
            EvaluateSingleDiagonal(piece, attackedSquares, -1, -1);
        }

        // O(sqrt(n))
        private void EvaluateSingleDiagonal(Piece piece, bool[,] attackedSquares, int rankSign, int fileSign)
        {
            int rank = 0;
            int file = 0;

            for (int i = 1; true; i++)
            {
                rank = piece.Rank + i * rankSign;
                file = piece.File + i * fileSign;
                if (rank < 0 || rank >= RankDim || file < 0 || file >= FileDim) break;
                attackedSquares[rank, file] = true;
                if (pieceLocations[rank, file]) break;
            }
        }

        // only called on solution found, performance not important here
        public override string ToString()
        {
            var board = "";
            var attackedSquares = GetCoveredSquares(false);

            for (int rank = RankDim-1; rank >= 0; rank--)
            {
                for (int file = 0; file < FileDim; file++)
                {
                    char? pieceType = pieces.FirstOrDefault(p => p.Rank == rank && p.File == file)?.Type;
                    board += pieceType ?? (attackedSquares[rank, file] ? '.' : 'x');  // yikes why did I write that this way
                }
                board += '\n';
            }
            return board;
        }

        // O(p)
        public string GetRepresentation()
        {
            string retval = "";

            for (int i = 0; i < pieces.Length - 1; i++)
            {
                retval += pieces[i].ToString() + ";";
            }

            retval += pieces[pieces.Length - 1];

            return retval;
        }

        class PawnConfigurationTracker
        {
            PawnConfigurationNode rootNode;
            public PawnConfigurationTracker()
            {
                rootNode = new PawnConfigurationNode();
            }

            // configuration must be sorted to avoid equivalents in the tree structure
            // caller must ensure this
            // O(p) as configuration.Count is max numPawns
            public bool TryAddPawnConfiguration(int[] configuration)
            {
                bool configurationAdded = false;
                var currentNode = rootNode;

                foreach (var position in configuration)
                {
                    if (!currentNode.ContainsKey(position)){
                        configurationAdded = true;
                        currentNode.Add(position, new PawnConfigurationNode());
                    }
                    currentNode = currentNode[position];
                }

                return configurationAdded;
            }

            class PawnConfigurationNode : Dictionary<int, PawnConfigurationNode> { }
        }
    }
}