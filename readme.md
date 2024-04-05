# dpenner1's Chess Domination Solver
--------

Though it's been generalized, the core algorithm and optimizations were coded to solve https://puzzling.stackexchange.com/q/2872/3936

It's an iterative search with optional filters over a set of given major pieces. Then if feasible, targeted pawn placement.
Assuming correct code, ALL solutions not filtered out will be found given the initial starting set (no filters means a completely exhaustive search).
Note: for now, it just outputs the placement of the initial starting set - you'll have to reverse engineer the pawn placement.

Algorithmic complexity for m = number of major pieces, p = number of pawns, n = number of squares: 

  - O( n<sup>m</sup>np<sup>2</sup>  +  n<sup>m</sup>sqrt(n)mp  +  n<sup>m</sup>p<sup>3</sup>log(p) ), I believe the first two terms are tight bounds, but better math could bring the last down

For m & p on order of O(1):

  - O(n<sup>m+1</sup>)

For m & p on order of O(sqrt(n)):

  - O(n<sup>sqrt(n)+2</sup>)

For m & p on order of O(n):

  - O(n<sup>n+3</sup>log(n)) = O(heat death of the universe)

Basically, this algorithm scales way better with board size (polynomial!) than number of pieces (worse than exponential!)

--------

Note: I'm coming back to this after at least five years. Random updates:

- From what I recall, I had effectively completed the use case for solving the StackExchange question, but wanted to improve and generalize for fun.
    - There's a configuration to allow a piece to cover its own square. From what I recall, this option is not at all well suited to the algorithm and is not performant.
    - The search set-up strings contain a |0-64| entry: This was intended for parallelization. While not yet fully implemented, it should allow for partitioning the work (though parallelization is currently possible across distinct major piece sets, just not within the selected major piece set yet).
    - Originally I had to manually specify the major pieces to be used for a run. Looks like I coded automatic generation of that, but can't remember having used it

- Looking through my old comments on algorithmic complexity, I had to correct several mistakes, so maybe there are more!
    - Most importantly, missed that initializing the chess board was O(n) instead of O(p)! Luckily there's only one place where this meaningfully contributes to higher complexity, and it might be possible to remove
    - Making known improvements should result in the third term in that Big-O analysis being dwarfed by the other two, at least in practice

- Copied the code into a .NET 7 solution, I believe I was previously using MonoDevelop .NET 4.x (Linux)

- Next things to do:
  - Get that parallelization working
  - Get the solution printout to include pawn placements
  - See about improving the pawn placement algo (including that O(n) board copy) 
      - Though this might be premature optimization, in practice only about 10-15% of board evaluations have pawns
  - See about a "bishops on opposite colors" restriction
  - See about adding opposing pieces (pawns target in the other direction)
  - Search resumption is not yet smooth (especially stats & found solutions)
  - Better symmetry detection, right now it just fixes one piece to the left half of the board to remove some symmetrical solutions
     - This just plain works though until you get to allowing 3 copies of a major piece. Then you could fix eg. 2 pieces to left half, but testing the eight bishops revealed bugginess here, probably the NextBoard iteration messes something up here
  - The algo aborts on pawn placement impossibility, but could we possibly abort even earlier, on major piece impossibility too?