Board Creation Prompt

Create a web based board editor that allows the user to select one board from the Boards table 
    (or create a new board).

Allow users to set all the parameters for the board, 
    size (x,y), 
    Laser Damage, 
    Phase Count.  
    Total Flags and Players will be set when squares are placed.

The board editor should have a grid interface where users can click to place squares.

Each square can be of different types (e.g., normal, obstacle, flag) and should have properties that can be edited (e.g., damage for laser squares).

The editor should also allow users to save their board configurations to the database and load existing boards for editing.

The editor should provide a preview of the board as it is being created, allowing users to see how their changes affect the layout and gameplay.

The editor should show a list of possible square types from install/BoardTemplate.srx
    When a square type is selected, the square type rotation and x/y position will be added or updated in the BoardItems table.
    The default attributes will be put into the boarditemactions table 

When a user adds a start position or flag, it should set the parameter to the next value starting with 1 and incrementing each time

If you have any questions, ask instead of making assumptions.

