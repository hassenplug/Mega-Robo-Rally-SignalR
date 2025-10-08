using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel; //INotifyPropertyChanged
using System.Xml.Serialization; // serializer

namespace MRR_CLG
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

            //OnPropertyChanged("FullLocation");

            //OnPropertyChanged("Direction");
            //OnPropertyChanged("X");
            //OnPropertyChanged("Y");

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

            switch (p_direction)
            {
                case Direction.Right: // +X
                    NewLocation.X += p_distance;
                    break;
                case Direction.Down: // + y
                    NewLocation.Y += p_distance;
                    break;
                case Direction.Left: // - X
                    NewLocation.X -= p_distance;
                    if (NewLocation.X < 0) NewLocation.X = 0;
                    break;
                case Direction.Up:   // -Y
                    NewLocation.Y -= p_distance;
                    if (NewLocation.Y < 0) NewLocation.Y = 0;
                    break;
                case Direction.None:
                default:
                    break;
            }

            return NewLocation;
        }

        public Direction Direction { get; set; }

        private int l_x = -1;
        public int X { get { return l_x; } set { l_x = value; if (l_x < 0) l_x = 0;  } }

        private int l_y = -1;
        public int Y { get { return l_y; } set { l_y = value; if (l_y < 0) l_y = 0;  } }

        private int l_index = 0;
        public int Index { get { return l_index; } set { l_index = value;   } }

        [XmlIgnore]
        public string Location { get { return "[" + X.ToString() + "][" + Y.ToString() + "]"; } set { } }

        [XmlIgnore]
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
