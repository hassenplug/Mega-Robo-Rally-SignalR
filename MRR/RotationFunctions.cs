using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Windows.Media;

namespace MRR_CLG
{
    public enum Direction
    {
        None = 0,
        Up = 1,
        Right = 2,
        Down = 3,
        Left = 4
    }

    public class RotationFunctions
    {

        //static public RotateTransform ImageRotation(Direction p_ImageDirection)
        //{
        //    int[] imgRot = { 0, 0, 90, 180, 270 };
        //    return new RotateTransform(imgRot[(int)p_ImageDirection]);
        //}

        static public Direction Rotate(int RotateDir, Direction StartingDirection)
        {
            int currentdir = (int)(StartingDirection);
            if (RotateDir == -2) RotateDir = 2;

            Direction[,] dirArray = {{Direction.None,Direction.None,Direction.None,Direction.None}, // none
            {Direction.Left,Direction.Up,Direction.Right,Direction.Down}, // up
            {Direction.Up,Direction.Right,Direction.Down,Direction.Left}, // right
            {Direction.Right,Direction.Down,Direction.Left,Direction.Up}, // down
            {Direction.Down,Direction.Left,Direction.Up,Direction.Right}}; // left

            return dirArray[currentdir, RotateDir + 1]; ;
        }

        static public int RotationDifference(Direction StartDirection, Direction EndDirection)
        {
            int newdirection = (int)EndDirection - (int)StartDirection;
            if (newdirection < -1) newdirection += 4;
            if (newdirection > 2) newdirection -= 4;
            return newdirection;
        }

        static public Direction SumDirections(Direction RobotDirection, Direction OptionDirection)
        {
            //int turnDifference = 
            int robot = ((int)RobotDirection + ((int)OptionDirection - 1))%4;
            if (robot == 0) robot = 4;

            return (Direction)robot;
        }

        /// <summary>
        /// This returns the direction of an optional weapon, given the player and option directions.
        /// In a case where the weapon is receiving damage from an Incoming Weapon, the last parameter should be true
        /// </summary>
        /// <param name="p_player"></param>
        /// <param name="p_option"></param>
        /// <param name="IncomingWeapon"></param>
        /// <returns></returns>
        static public Direction GetOptionDirection(Player p_player, OptionCard p_option, bool IncomingWeapon = false)
        {
            Direction OptDir = SumDirections(p_player.CurrentPos.Direction, p_option.OptionDirection);
            if (IncomingWeapon) OptDir = RotationFunctions.Rotate(2, OptDir);
            return OptDir;
        }

        static public Direction IncomingDirection(Direction DirectionIN)
        {
            return Rotate(2,DirectionIN);
        }

        static public int MovementOffsetX(Direction FacingDirection)
        {
            switch (FacingDirection)
            {
                case Direction.Up:
                case Direction.Down:
                    return 0;
                case Direction.Right:
                    return 1;
                case Direction.Left:
                    return -1;
                case Direction.None:
                default:
                    return 0;
            }
        }

        static public int MovementOffsetY(Direction FacingDirection)
        {
            switch (FacingDirection)
            {
                case Direction.Up:
                    return -1;
                case Direction.Down:
                    return 1;
                case Direction.Right:
                case Direction.Left:
                case Direction.None:
                default:
                    return 0;
            }
        }

    }
}
