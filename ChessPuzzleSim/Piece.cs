using System;

namespace dpenner1.Chess.ChessDominationSolver
{
    // Everything here is O(1)
    public class Piece
    {
        const int RankDim = SearchSettings.RankDim;
        const int FileDim = SearchSettings.FileDim;

        public Piece(string representation)
        {
            if (representation.Length != 3) throw new ArgumentException("Invalid Piece representation");

            Type = representation[0];
            File = representation[1] - 'a';
            Rank = int.Parse(representation[2].ToString()) - 1;
        }

        public Piece(char type, int rank, int file)
        {
            Type = type;
            Rank = rank;
            File = file;
        }

        // deep copy constructor
        public Piece(Piece p) : this(p.Type, p.Rank, p.File) { }

        public char Type { get; }
        public int File { get; set; }   // stored 0-7
        public int Rank { get; set; }   // stored 0-7
        public int Position => Rank * FileDim + File; //sometimes a single number is more convenient
        public bool OnBoard { get; set; }

        public int Points
        {
            get
            {
                switch (Type)
                {
                    case 'Q': return 9; // making a choice
                    case 'R': return 5;
                    case 'K': return 4; // arbitrary
                    case 'B': return 3;
                    case 'N': return 3;
                    case '^': return 1;
                    default: throw new InvalidOperationException("Invalid piece type");
                }
            }
        }

        public void MoveToNextPosition()
        {
            File++;
            if (File == FileDim)
            {
                MoveToNextRank();
            }
        }

        public void MoveToNextRank()
        {
            if (Rank == RankDim - 1) throw new InvalidOperationException("Tried to move piece off board");
            File = 0;
            Rank++;
        }

        public void MoveToPiece(Piece p)
        {
            File = p.File;
            Rank = p.Rank;
        }

        public void MoveAfterPiece(Piece p)
        {
            MoveToPiece(p);
            MoveToNextPosition();
        }

        public void MoveToA1()
        {
            File = 0;
            Rank = 0;
        }

        public bool IsOnEdge => Rank == 0 || Rank == RankDim - 1 || File == 0 || File == FileDim - 1;

        public override string ToString()
        {
            return Type.ToString() + ((char)(File + 'a')).ToString() + (Rank + 1);
        }
    }
}