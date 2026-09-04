using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel; //INotifyPropertyChanged
using System.Xml.Serialization; // serializer

namespace MRR
{
    /*
     * locations for robots
     * -Current
     * -After move
     * -End Of Turn
     */
    public class RobotLocation
    {
        static private Direction DefaultDirection = Direction.Right;

        public RobotLocation(Direction p_Dir, int p_X, int p_Y)
        {
            Direction = p_Dir;
            X = p_X;
            Y = p_Y;
        }

        public RobotLocation(RobotLocation p_NewLocation)
            : this(p_NewLocation.Direction, p_NewLocation.X, p_NewLocation.Y)
        {
        }

        public RobotLocation(BoardElement p_Square)
            : this(p_Square.Rotation, p_Square.BoardCol, p_Square.BoardRow)
        {
        }

        public RobotLocation()
            : this(DefaultDirection, -1, -1)
        {
        }

        public RobotLocation(Direction p_Dir, int p_X, int p_Y, int p_Index): this(p_Dir, p_X, p_Y)
        {
            Index = p_Index;
        }

        public void SetLocation(RobotLocation p_NewLocation)
        {
            Direction = p_NewLocation.Direction;
            X = p_NewLocation.X;
            Y = p_NewLocation.Y;

        }

        public RobotLocation CalcNewLocation(int p_distance = 1, Direction p_direction = Direction.None) //RobotLocation p_CurrentLocation)
        {
            if (p_direction == Direction.None)
            {
                p_direction = Direction;
            }

            // check direction
            // move p_distance based on direction

            RobotLocation NewLocation = new RobotLocation(Direction, X, Y);
            var (AddX, AddY) = RotationFunctions.MovementOffset(p_direction);
            NewLocation.X += AddX * p_distance;
            NewLocation.Y += AddY * p_distance;

            return NewLocation;
        }

        public Direction Direction { get; set; }

        // No clamping here: -1 is the deliberate "unset" sentinel the parameterless
        // constructor uses (see below), and a genuinely negative position (a robot moving
        // off the left/top edge of the board) has to stay distinguishable from square 0 --
        // clamping both to 0 collided the two and corrupted CommandItem.EndPos (see
        // CommandList.cs). DamageSpread (CreateCommands.cs) already relies on negative X/Y
        // being observable to detect leaving the board; bounds-checking belongs to callers
        // like it and CalcMoveDistance, not to this setter.
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;

        private int l_index = 0;
        public int Index { get { return l_index; } set { l_index = value;   } }

        public string Location { get { return "[" + X.ToString() + "][" + Y.ToString() + "]"; } set { } }

        public string FullLocation { get { return ToString(); } set { } }

        override public string ToString()
        {
            if (Index != 0) return Location + Index.ToString();

            string dir = Direction.ToString();
            switch (Direction)
            {
                case Direction.None: dir = "?"; break;
                case Direction.Up: dir = "^"; break;
                case Direction.Right: dir = ">"; break;
                case Direction.Down: dir = "V"; break;
                case Direction.Left: dir = "<"; break;
            }

            return dir + Location; 
        }
    }
}
