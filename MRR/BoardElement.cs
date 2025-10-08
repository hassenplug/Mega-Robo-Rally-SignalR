using System.Collections.ObjectModel;
using System.ComponentModel; // INotifyPropertyChanged
using System.Xml.Serialization; // serializer


namespace MRR_CLG
{

    #region Board Element Collection
    public class BoardElementCollection  : INotifyPropertyChanged  // : ObservableCollection<BoardElement>
    {
        public event PropertyChangedEventHandler PropertyChanged;
        // Create the OnPropertyChanged method to raise the event
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    

        public BoardElementCollection(int Columns, int Rows) : this()
        {
            // create elements here...
            BoardCols = Columns;
            BoardRows = Rows;

        }

        public BoardElementCollection()
            : base()
        {
            BoardElements = new ObservableCollection<BoardElement>();
        }

        // properties for the collection, here
        //[XmlAttribute("Cols")]
        public int BoardCols { get; set; }

        //[XmlAttribute("Rows")]
        public int BoardRows { get; set; }

        public string BoardName { get; set; }

        public int OptionsOnStartup { get; set; } = -1;

        private int _laserDamage = 1;
        public int LaserDamage { get { return _laserDamage; } set { _laserDamage = value; } }
        //public int LaserDamage { get { return _laserDamage; } set { _laserDamage = value; OnPropertyChanged("LaserDamage"); } }

        public int Lives { get; set; } = 3; // { get { return _lives; } set { _lives = value; } }

        public int TotalFlags {get;set;}

        public int GameType {get;set;}

        public ObservableCollection<BoardElement> BoardElements { get; set; }

        [XmlIgnore]
        public string FileData
        {
            get
            {
                return "0," + BoardCols.ToString() + "," + BoardRows.ToString() + "," + BoardName;
            }
            set
            {

            }
        }


        public BoardElement SetSquare(int p_col, int p_row, SquareType p_squaretype, Direction p_squaredirection, BoardActionsCollection p_squareactions)
        {
            //BoardElement l_square = c_wholeboard[p_col, p_row];
            BoardElement l_square = GetSquare(p_col, p_row);
            if (l_square == null)
            {
                l_square = new BoardElement(p_col, p_row, p_squaretype, p_squaredirection);
                //l_square = new BoardElement();
                //c_wholeboard[p_col, p_row] = l_square;
                this.BoardElements.Add(l_square);
            }

            l_square.Type = p_squaretype;
            l_square.Rotation = p_squaredirection;

        //if (l_square.ActionList != null)
        //{
            l_square.ActionList = p_squareactions;
        //}

            l_square.Update();

            return l_square;
        }

        public void SetSquare(BoardElement p_square)
        {
            // copy this square to existing square...
            BoardElement newsquare = SetSquare(p_square.BoardCol, p_square.BoardRow, p_square.Type, p_square.Rotation, p_square.ActionList);
            newsquare.TotalCount = p_square.TotalCount;
        }

        public BoardElement GetSquare(int Col, int Row)
        {
            return GetSquare(be => ((be.BoardCol == Col) && (be.BoardRow == Row)));
        }

        public BoardElement GetSquare(SquareType squaretype)
        {
            return GetSquare(be => be.Type == squaretype);
        }

        public BoardElement GetSquare(RobotLocation location)
        {
            return GetSquare(be => ((be.BoardCol == location.X) && (be.BoardRow == location.Y)));
        }

        public BoardElement GetSquare(Func<BoardElement,bool> GetFunction)
        {
            IEnumerable<BoardElement> thisList = this.BoardElements.Where(GetFunction);
            //if (thisList == null)
            if (thisList.Count() == 0)
            {
                return null;
            }

            return thisList.First();
        }

        public BoardElement GetFlagSquare(int p_FlagNumber)
        {
            return GetSquare(be => be.ActionList.Count(al => (
                (al.SquareAction == SquareAction.Flag)
                    || (al.SquareAction == SquareAction.TouchFlag)
                    || (al.SquareAction == SquareAction.TouchKotHFlag)
                    || (al.SquareAction == SquareAction.TouchLastManFlag)) 
                && (al.Parameter == p_FlagNumber)) > 0);
        }

        public void RotateRight()
        {
            int StartingRows = this.BoardRows;
            int StartingColumns = this.BoardCols;

            this.BoardRows = StartingColumns;
            this.BoardCols = StartingRows;

            foreach (BoardElement onelement in this.BoardElements)
            {
                int newrow = onelement.BoardCol;
                int newcol = StartingRows - onelement.BoardRow - 1;
                onelement.BoardRow = newrow;
                onelement.BoardCol = newcol;
                onelement.Rotate(1);
                onelement.Update();

            }
        }
    }
    #endregion

    #region Board Elements
    public class BoardElement : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;


        // Create the OnPropertyChanged method to raise the event
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
        // properties for the class here

        public BoardElement(string[] newline)
        {
            int maxcount = -1;
            //if (newline.Count() > 5)
            if (newline[0] == "2")
            {
                maxcount = Convert.ToInt16(newline[5]);
            }

            SetBoardElement(Convert.ToInt16(newline[1]),Convert.ToInt16(newline[2]),(SquareType)Convert.ToInt16(newline[3]),(Direction)Convert.ToInt16(newline[4]), maxcount);
        }

        public BoardElement(int Col, int Row, SquareType img, Direction dir)
        {
            SetBoardElement(Col, Row, img, dir, -1);
        }

        public BoardElement(int img, int total)
        {
            //SetBoardElement(0, 0, img, Direction.None,false);
        }

        public BoardElement()
        {
            SetBoardElement(0, 0, SquareType.Blank, Direction.None, -1);
        }

        public void SetBoardElement(int col, int row, SquareType img, Direction dir, int MaxCount)
        {
            //BoardSquareLocation = new RobotLocation(dir, col, row);
            BoardCol = col;
            BoardRow = row;
            Type = img;
            Rotation = dir;
            TotalCount = MaxCount;
            TotalUsed = 0;
            //BuildImage();
            //BuildPanel();
            //OnPropertyChanged("Panel");
        }

        public BoardElement SetSquare(BoardElement newElement)
        {
            Type = newElement.Type;
            Rotation = newElement.Rotation;
            // add actions
            ActionList.Clear();
            foreach (BoardAction thisaction in newElement.ActionList)
            {
                ActionList.Add(thisaction);
            }
            //BuildImage();
            //BuildPanel();
            return this;
        }

        //private DockPanel c_panel = new DockPanel();
        //private Grid c_panel = new Grid();
        private BoardActionsCollection c_actionlist = new BoardActionsCollection();
        private Direction c_rotation = Direction.None;
        //public string BoardImage { get; set; }
        //public Direction Rotation { get; set; }
        //[XmlAttribute("Col")]
        public int BoardCol { get; set; }
        //[XmlAttribute("Row")]
        public int BoardRow { get; set; }

        //public RobotLocation BoardSquareLocation { get; set; }

        //public int BoardImg { get; set; }
        public SquareType Type { get; set; }

        [XmlIgnore]
        public int TotalCount { get; set; }
        //public int TotalUsed { get; set; }
        //public int TotalRemaining { get; set; }
        //private Image c_image = new Image();

        private int l_totalused = 0;
        [XmlIgnore]
        public int TotalUsed
        {
            get { return l_totalused; }
            set
            {
                l_totalused = value;
                //OnPropertyChanged("TotalUsed");
                //OnPropertyChanged("TotalRemaining");
                //OnPropertyChanged("BGColor");
                //OnPropertyChanged("Panel");
            }
        }


        //public BoardActionsCollection ActionList { get; set; }

        public Direction Rotation
        {
            get { return c_rotation; }
            set
            {
                c_rotation = value;
                // look at action list...
                UpdateActionRotation();
                Update();
            }
        }

        /// <summary>
        /// will change rotation.  RotateDir = -1,0,1
        /// </summary>
        /// <param name="RotateDir"></param>
        /// <returns></returns>
        public Direction Rotate(int RotateDir)
        {

            Rotation = RotationFunctions.Rotate(RotateDir, Rotation);
            return Rotation;
        }

        //static public Direction Rotate(int RotateDir, Direction StartingDirection)
        //{
        //    int currentdir = (int)(StartingDirection);
        //    Direction[,] dirArray = {{Direction.None,Direction.None,Direction.None,Direction.None}, // none
        //    {Direction.Left,Direction.Up,Direction.Right,Direction.Down}, // up
        //    {Direction.Up,Direction.Right,Direction.Down,Direction.Left}, // right
        //    {Direction.Right,Direction.Down,Direction.Left,Direction.Up}, // down
        //    {Direction.Down,Direction.Left,Direction.Up,Direction.Right}}; // left

        //    return dirArray[currentdir, RotateDir + 1];;
        //}


        public BoardActionsCollection ActionList
        {
            get { return c_actionlist; }
            set
            {
                if (value != null)
                {
                    c_actionlist = value;
                }
                else
                {
                    c_actionlist = new BoardActionsCollection();
                }
                UpdateActionRotation();
            }
        }

        [XmlIgnore]
        public int ActionCount
        {
            get { return c_actionlist.Count(); }
            set { }
        }



        public void AddAction(BoardAction p_action)
        {
            this.ActionList.Add(p_action);
            //OnPropertyChanged("Panel");
            Update();
        }

        public void AddWall(Direction Rotation)
        {
            BoardAction newwall = new BoardAction();
            newwall.ActionSequence = 0;
            newwall.Parameter = (int)Rotation;
            newwall.Phase = 31;
            newwall.SquareAction = SquareAction.BlockDirection;
            AddAction(newwall);
        }

        public void RotateWalls(int direction = 1)
        {
            ActionList.Where(al => al.SquareAction == SquareAction.BlockDirection).Select(al => al.Parameter =(int)RotationFunctions.Rotate(direction, (Direction)al.Parameter)).ToList();
            Update();
        }

        public void UpdateActionRotation()
        {
            //selectedRobots = m_ConnectedRobots.Where(cr => cr.connectstatus == disconnectStatus);

            var RotatedActions = ActionList.Where(al => al.ActionIncludesRotation());
            foreach (BoardAction RA in RotatedActions)
            {
                RA.Parameter = (int)Rotation;
            }
        }

        // panel includes rotation, walls, and Flag/Start points
        // build image only includes image
        /*
        private Grid BuildPanel()
        {
            c_panel.Children.Clear();

            if (BuildImage() == null)
            {
                TextBlock l_textblock = new TextBlock();
                l_textblock.Text = this.Type.ToString() + "," + this.Rotation.ToString();
                c_panel.Children.Add(l_textblock);

                foreach (BoardAction l_boardaction in this.ActionList)
                {
                    TextBlock l_block = new TextBlock();
                    l_block.Text = l_boardaction.SquareAction.ToString() + "," + l_boardaction.Parameter.ToString() + "," + l_boardaction.ActionSequence.ToString() + "," + l_boardaction.Phase.ToString();
                    c_panel.Children.Add(l_block);

                }
            }
            else
            {
                c_panel.Children.Add(c_image);
                string UseText = ""; // ActionString();
                Brush bgcolor = Brushes.Black;

                if (TotalCount > 0)
                {
                    //UseText += "T" + TotalCount.ToString() + "R" + TotalRemaining.ToString();
                    UseText += "R" + TotalRemaining.ToString();
                    if (TotalRemaining < 0)
                    {
                        bgcolor = Brushes.Red;
                    }
                }
                else
                {


                    foreach (BoardAction l_boardaction in this.ActionList)
                    {
                        switch (l_boardaction.SquareAction)
                        {
                            case SquareAction.Flag: UseText += "F" + l_boardaction.Parameter.ToString(); break;
                            case SquareAction.PlayerStart:
                                if (l_boardaction.Parameter != 11)
                                {
                                    UseText += "S" + l_boardaction.Parameter.ToString();
                                }
                                break;
                            case SquareAction.BlockDirection:
                                // use this to create wall //

                                string wallImageName = Properties.Settings.Default.BoardImagesPath + "Over200.png";
                                Image l_wallimage = new Image();
                                Uri walliconUri = new Uri(wallImageName, UriKind.Relative);

                                try
                                {
                                    l_wallimage.Source = new BitmapImage(walliconUri);
                                }
                                catch
                                {
                                }

                                l_wallimage.LayoutTransform = RotationFunctions.ImageRotation((Direction)l_boardaction.Parameter);

                                c_panel.Children.Add(l_wallimage);

                                // or this ///
                                //if (this.Type != SquareType.Walls)
                                //{
                                //    string[] lwalls = { "?", "V", "<", "^", ">" };
                                //    UseText += "wall:" + lwalls[l_boardaction.Parameter];
                                //}
                                break;
                            case SquareAction.Damage:
                                if ((l_boardaction.Parameter != 100) && (l_boardaction.Parameter != -1))
                                {
                                    UseText += "D";
                                    if (l_boardaction.Parameter > 0)
                                    {
                                        UseText += "+";
                                    }
                                    UseText += l_boardaction.Parameter;
                                }
                                break;
                            default: break; // UseText = "P" + l_boardaction.Parameter.ToString(); break;
                        }
                    }
                }

                if (UseText.Length > 0)
                {
                    TextBlock l_textblock = new TextBlock();
                    l_textblock.Text = UseText;
                    l_textblock.HorizontalAlignment = HorizontalAlignment.Right;
                    l_textblock.VerticalAlignment = VerticalAlignment.Bottom;
                    l_textblock.Foreground = Brushes.White;
                    l_textblock.Background = bgcolor;
                    //l_textblock.Foreground = Brushes.Black;
                    c_panel.Children.Add(l_textblock);
                }
            }


            Grid.SetColumn(c_panel, BoardCol);
            Grid.SetRow(c_panel, BoardRow);

            return c_panel;
        }*/

        ///// <summary>
        ///// this function is currently not working
        ///// </summary>
        //private void BuildPanelWallOverlay()
        //{
        //    //Grid c_panel = new Grid();
        //    c_panel.Children.Clear();

        //    // overlay walls
        //    //if (ActionList.Count(al => al.SquareAction == SquareAction.BlockDirection) > 0)
        //    if (ActionList.Count(al => al.ActionIncludesImage()) > 0)
        //    {
        //        //BoardAction thisaction = ActionList.First(al => al.SquareAction == SquareAction.BlockDirection);
        //        BoardAction thisaction = ActionList.First(al => al.ActionIncludesImage());

        //        string wallImageName = Properties.Settings.Default.BoardImagesPath + "Over" + ((int)Type).ToString() + ".png";
        //        Image l_wallimage = new Image();
        //        Uri walliconUri = new Uri(wallImageName, UriKind.Relative);

        //        try
        //        {
        //            l_wallimage.Source = new BitmapImage(walliconUri);
        //        }
        //        catch
        //        {
        //        }

        //        l_wallimage.LayoutTransform = RotationFunctions.ImageRotation((Direction)thisaction.Parameter);

        //        c_panel.Children.Add(l_wallimage);
        //        //Grid.SetColumn(c_panel, BoardCol);
        //        //Grid.SetRow(c_panel, BoardRow);
        //        //return;
        //    }


        //    string ImageName = Properties.Settings.Default.BoardImagesPath + "board" + ((int)Type).ToString() + ".jpg";
        //    Image l_image = new Image();
        //    Uri iconUri = new Uri(ImageName, UriKind.Relative);

        //    try
        //    {
        //        l_image.Source = new BitmapImage(iconUri);

        //        l_image.LayoutTransform = RotationFunctions.ImageRotation(Rotation);

        //        c_panel.Children.Add(l_image);
        //    }

        //    catch // (ApplicationException ex) // there some other kind of error, causing the system to crash.
        //    {

        //        //MessageBox.Show("The program will now crash.");
        //        //return null;
        //    }



        //    // overlay flags & Starts

        //    Grid.SetColumn(c_panel, BoardCol);
        //    Grid.SetRow(c_panel, BoardRow);
        //    //return c_panel;


        //}


        //private Grid BuildPanelImageOLD()
        //{
        //    Grid g_GridPanel = new Grid();
        //    g_GridPanel.Children.Clear();

        //    // overlay walls
        //    //if (ActionList.Count(al => al.SquareAction == SquareAction.BlockDirection) > 0)
        //    if (ActionList.Count(al => al.ActionIncludesImage()) > 0)
        //    {
        //        //BoardAction thisaction = ActionList.First(al => al.SquareAction == SquareAction.BlockDirection);
        //        BoardAction thisaction = ActionList.First(al => al.ActionIncludesImage());

        //        string wallImageName = Properties.Settings.Default.BoardImagesPath + "Over" + ((int)Type).ToString() + ".png";
        //        Image l_wallimage = new Image();
        //        Uri walliconUri = new Uri(wallImageName, UriKind.Relative);

        //        try
        //        {
        //            l_wallimage.Source = new BitmapImage(walliconUri);
        //        }
        //        catch
        //        {
        //        }

        //        l_wallimage.LayoutTransform = RotationFunctions.ImageRotation((Direction)thisaction.Parameter);

        //        g_GridPanel.Children.Add(l_wallimage);
        //    }


        //    string ImageName = Properties.Settings.Default.BoardImagesPath + "board" + ((int)Type).ToString() + ".jpg";
        //    Image l_image = new Image();
        //    Uri iconUri = new Uri(ImageName, UriKind.Relative);

        //    try
        //    {
        //        l_image.Source = new BitmapImage(iconUri);

        //        l_image.LayoutTransform = RotationFunctions.ImageRotation(Rotation);

        //        g_GridPanel.Children.Add(l_image);
        //    }

        //    catch // (ApplicationException ex) // there some other kind of error, causing the system to crash.
        //    {

        //        //MessageBox.Show("The program will now crash.");
        //        //return null;
        //    }



        //    // overlay flags & Starts

        //    Grid.SetColumn(g_GridPanel, BoardCol);
        //    Grid.SetRow(g_GridPanel, BoardRow);
        //    return g_GridPanel;


        //}

            /*
        private Image BuildImage()
        {
            string ImageName = Properties.Settings.Default.BoardImagesPath + "board" + ((int)Type).ToString() + ".jpg";

            c_image = new Image();

            Uri iconUri = new Uri(ImageName, UriKind.Relative);
            
            try
            {
                c_image.Source = new BitmapImage(iconUri);

            }
            catch // (ApplicationException ex) // there some other kind of error, causing the system to crash.
            {

                //MessageBox.Show("The program will now crash.");
                return null ;
            }


            c_image.LayoutTransform = RotationFunctions.ImageRotation(Rotation);

            return c_image;

        }*/

        [XmlIgnore]
        public int TotalRemaining
        {
            get {return TotalCount-TotalUsed;}
            set {}
        }


        public void Update()
        {
            //BuildImage();
            //OnPropertyChanged("Panel");
            //OnPropertyChanged("Rotation");
            //OnPropertyChanged("SquareImage");
            //OnPropertyChanged("ActionCount");
            //OnPropertyChanged("TotalRemaining");
            //OnPropertyChanged("BGColor");
        }

        [XmlIgnore]
        public string Location { get { return "[" + BoardCol.ToString() + "][" + BoardRow.ToString() + "]"; } set { } }


    }
    #endregion

    #region Board Actions Collection
    public class BoardActionsCollection : ObservableCollection<BoardAction>
    {

        public BoardActionsCollection()
            : base()
        {
        }

        public BoardActionsCollection(BoardActionsCollection p_copyActions)
            :base()
        {
            if (p_copyActions != null)
            {
                foreach (BoardAction thisaction in p_copyActions)
                {
                    BoardAction newAction = new BoardAction(thisaction);

                    this.Add(newAction);
                }
            }

        }

    }
    #endregion

    #region Board Actions
    public class BoardAction: INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;


        // Create the OnPropertyChanged method to raise the event
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
        // properties for the class here

        //SquareAction l_squareaction = SquareAction.None;
        //int l_Parameter = 0;
        //int l_ActionSequence = 0;

        public SquareAction SquareAction { get; set; }
        public int ActionSequence { get; set; }
        private int l_Phase = 0;
        public int Phase
        {
            get
            {
                return l_Phase;
            }
            set
            {
                l_Phase = value;
                //OnPropertyChanged("Phase");
                //OnPropertyChanged("ShowPhase");
            }
        }
        private int l_parameter;

        [XmlIgnore]
        public int RobotID { get; set; }
        [XmlIgnore]
        public int SquareX { get; set; }
        [XmlIgnore]
        public int SquareY { get; set; }
        

        public int Parameter
        {
            get { return l_parameter; }
            set
            {
                l_parameter = value;
                //OnPropertyChanged("Parameter");
            }
        }

        [XmlIgnore]
        public string FileData
        {
            get
            {
                return ((int)SquareAction).ToString() + "," + Parameter.ToString() + "," + ActionSequence.ToString() + "," + Phase.ToString();
            }
        }

        [XmlIgnore]
        public string ShowPhase
        {
            get
            {
                return PhaseActiveText(1) + PhaseActiveText(2) + PhaseActiveText(3) + PhaseActiveText(4) + PhaseActiveText(5) + "(" + Phase.ToString() + ")";
            }
        }

        private string PhaseActiveText(int selectPhase)
        {
            //int calcphase = (int)Math.Pow( 2,selectPhase-1);
            if (PhaseActive(selectPhase))
            {
                return selectPhase.ToString();
            }
            else
            {
                return "-";
            }
        }

        public bool PhaseActive(int selectPhase)
        {
            return PhaseFunctions.GetActive(Phase, selectPhase);
            //int calcphase = (int)Math.Pow( 2,selectPhase-1);
            //return ((Phase & calcphase) != 0);
        }

        public BoardAction() // required for seralization
        {
        }

        public BoardAction(string[] newline) // create new board action from file string
        {
            SquareAction = (SquareAction)Convert.ToInt16(newline[0]);
            Parameter = Convert.ToInt16(newline[1]);
            ActionSequence = Convert.ToInt16(newline[2]);
            Phase = Convert.ToInt16(newline[3]);
        }

        public BoardAction(BoardAction p_CurrentAction)
        {
            SquareAction = p_CurrentAction.SquareAction;
            Parameter = p_CurrentAction.Parameter;
            ActionSequence = p_CurrentAction.ActionSequence;
            Phase = p_CurrentAction.Phase;
        }

        public BoardAction(BoardAction p_CurrentAction, int p_RobotID, int p_SquareX, int p_SquareY)
            :this(p_CurrentAction)
        {
            RobotID = p_RobotID;
            SquareX = p_SquareX;
            SquareY = p_SquareY;
        }

        public BoardAction(OptionCard p_OptionCard): this()
        {
            SquareAction = SquareAction.PlayOptionCard;
            Parameter = p_OptionCard.ID;
            ActionSequence = p_OptionCard.ActionSequence;
            Phase = p_OptionCard.PhasePlayed;
            RobotID = p_OptionCard.Owner;
            //SquareX = p_OptionCard.Owner
            //SquareY = 0;
        }

        public BoardAction(SquareAction p_SquareAction, int p_Parameter, int p_Sequence, int p_Phase)
            : this()
        {
            SquareAction = p_SquareAction;
            Parameter = p_Parameter;
            ActionSequence = p_Sequence;
            Phase = p_Phase;

        }

        /// <summary>
        /// some actions require a direction that changes with image rotation...
        /// </summary>
        /// <returns></returns>
        public bool ActionIncludesRotation()
        {
            switch (SquareAction)
            {
                case SquareAction.Move:
                case SquareAction.BlockDirection:
                //case SquareAction.PlayerStart:
                //case SquareAction.Flag:
                    return true;
                default:
                    return false;
            }
        }

        public bool ActionIncludesImage()
        {
            switch (SquareAction)
            {
                case SquareAction.BlockDirection:
                    return true;
                default:
                    return false;
            }
        }

    }
    #endregion

    #region Board enums
    public enum SquareAction
    {
        BoardDimension = 0, // Board definition: 0,X,Y
        SquareLocation = 1, // Block Definition: 1,X,Y,Type,Rotation
        SquareTemplate = 2, // Template for new block: 2,Template X, Template Y (x,y must be unique) ,Square Type, Rotatable (0=no), count of available
        PlayerLocation = 3, // Stored location of player in game 3,X,Y, Lives, Damage
        Unknown = 10,
        None = 11,
        Move = 12,
        Rotate = 13,
        Damage = 14,
        Archive = 15,
        Flag = 16,
        Option = 17,  // land on option square
        BlockDirection = 18,
        PlayerStart = 19,

        // other game commands
        Dead = 20,
        RobotPush = 21,
        LostLife = 22,
        ExplosiveDamage = 23,
        DealCard = 24, // deal move card
        PhaseStart = 30,
        PhaseStep = 31,
        PhaseEnd = 32,
        LogData = 40,
        GameWinner = 41,
        Card = 42,
        PlayOptionCard = 43,
        BeginBoardEffects = 49,
        PushedMove = 50,
        PushedMoveRotate = 51,  // this may not be used
        BoardMove = 52,
        BoardMoveRotate = 53,  // this may not be used
        BoardRotate = 54,

        Water = 55,

        DeletedMove = 56,
        StartBotMove = 57,
        StopBotMove = 58,
        FireCannon = 60,
        Randomizer = 61,
        SetPlayerStatus = 63,
        DeathPoints = 64,

        // options
        DealOptionCard = 65,
        DestroyOptionCard = 66,
        OptionCountSet = 67,
        SetDamagePointTotal = 68,
        SetEnergy = 69,

        // bluetooth
        BTConnect = 70,
        BTDisconnect = 71,
        DealSpamCard = 73,

        // options
        Mine = 80,
        LayBridge = 81,
        SetShutDownMode = 82,


        // command to robot
        TouchFlag = 83,
        TouchKotHFlag = 84,
        TouchLastManFlag = 85,

        // special
        SetCurrentGameData = 91,
        SetButtonText = 92,
        //UploadPrograms = 99,
        EndOfGame = 95,
        DeleteRobot = 96,
        SetGameState = 97,
		ShutDownGame = 98,
    }

    public enum SquareType
    {
        Blank = 0,
        NormalBelt = 10,
        NormalTurnCW = 11,
        NormalTurnCCW = 12,
        FastBelt = 20,
        FastTurnCW = 21,
        FastTurnCCW = 22,
        GearCW = 31,
        GearCCW = 32,
        Pit = 40,
        TrapDoor = 41,
        Edge = 42,
        CornerEdge = 43,
        Pusher = 50,
        Water = 55,
        Cannon = 60,  // laser
        Randomizer = 61,
        Crusher = 70,
        Flamer = 80,
        Wrench = 90,
        WrenchHammer = 91,
        Flag = 100,
        StartSquare = 110,
        Walls = 200
    }
    #endregion

}
