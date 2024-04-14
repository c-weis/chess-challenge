# Chess Coding Challenge (C#)
This is mine and [microbundle's](https://github.com/microbundle) attempt at Sebastian Lague's 
[chess coding challenge](https://youtu.be/iScy18pVR58). 

We did not partake in the original challenge, but had a go at it without peeking at other people's submission.

## MyBot
The bot uses basic alpha-beta pruning and an evaluation based on counting material and giving slight preference to advanced pawns, pieces in the centre and a safe king. We also implented a basic version of quiescence search - once the original depth is hit, we search captures and checks only for a few more moves. One of our bots greatest challenges proved to be time management. It seems our `DepthDecider` function is not smart enough at deciding how far to look when.

## Bonus features
Of course, all the code relevant to the challenge is contained in `MyBot.cs`. However, we also modified some of the UI code to allow adding more EvilBots. Further, after MyBot chooses a move, the UI figures out its preferred line and displays it in the console.
