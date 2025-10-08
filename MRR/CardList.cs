using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MRR_CLG
{
    public class CardList : ObservableCollection<MoveCard>
    {

        public CardList()
        {
            // set of cards
            BuildDictionary();

        }

        public CardList(IEnumerable<MoveCard> ExistingList):this()
        {
            foreach (MoveCard thiscard in ExistingList)
            {
                this.Add(thiscard);
            }
        }

        private Dictionary<MoveCard.tCardType, Tuple<int, string, string, int>> cardDictionary;

        /// <summary>
        /// build the dictionary for looking up values
        /// </summary>
        private void BuildDictionary()
        {
            // tuple = 
            // item1 = value from file,
            // item2 = description, 
            // item3 = short text, 
            // item4 = distance value
            cardDictionary = new Dictionary<MoveCard.tCardType, Tuple<int, string, string, int>>();
            cardDictionary.Add(MoveCard.tCardType.Unknown,  Tuple.Create(0, "Card Type unknown", "-", 0));
            cardDictionary.Add(MoveCard.tCardType.UTurn,    Tuple.Create(0, "U-Turn",      "U", 2));
            cardDictionary.Add(MoveCard.tCardType.RTurn,    Tuple.Create(0, "Right Turn",  "R", 1));
            cardDictionary.Add(MoveCard.tCardType.LTurn,    Tuple.Create(0, "Left Turn",   "L",-1));
            cardDictionary.Add(MoveCard.tCardType.Back1,    Tuple.Create(0, "Backward 1",  "B", -1));
            cardDictionary.Add(MoveCard.tCardType.Forward1, Tuple.Create(0, "Forward 1",   "1", 1));
            cardDictionary.Add(MoveCard.tCardType.Forward2, Tuple.Create(0, "Forward 2",   "2",2));
            cardDictionary.Add(MoveCard.tCardType.Forward3, Tuple.Create(0, "Forward 3",   "3",3));
            cardDictionary.Add(MoveCard.tCardType.Again,    Tuple.Create(0, "Again",       "A",0));
            cardDictionary.Add(MoveCard.tCardType.PowerUp,  Tuple.Create(0, "Power Up",    "P",0));
            cardDictionary.Add(MoveCard.tCardType.Spam,     Tuple.Create(0, "Spam",        "S",0));
            cardDictionary.Add(MoveCard.tCardType.Haywire,  Tuple.Create(0, "Haywire",     "H",0));
            cardDictionary.Add(MoveCard.tCardType.Option,   Tuple.Create(0, "Option Card", "O",0));
        }

        public int GetCardValue(MoveCard p_card)
        {
            return cardDictionary[p_card.Type].Item4; 
        }

        public string GetCardText(MoveCard p_card)
        {
            return cardDictionary[p_card.Type].Item3;
        }
    }


    /*
     * Additional Info
     * Function Move Cards from next owner (clear next owner)
     */

    public class MoveCard 
    {
        public enum tCardType
        {
            [Description("Card Type unknown")] 
            Unknown = 0,
            [Description("U-Turn")]
            UTurn = 1,
            [Description("Right Turn")]
            RTurn = 2,
            [Description("Left Turn")]
            LTurn = 3,
            [Description("Backward 1")]
            Back1 = 4,
            [Description("Forward 1")]
            Forward1 = 5,
            [Description("Forward 2")]
            Forward2 = 6,
            [Description("Forward 3")]
            Forward3 = 7,
            [Description("Again")]
            Again = 8,
            [Description("Power Up")]
            PowerUp = 9,
            [Description("Spam")]
            Spam = 10,
            [Description("Haywire")]
            Haywire = 11,
            [Description("Option Card")]
            Option = 30,
        }


        public MoveCard(int p_CardID, tCardType p_CardType)
        {
            // create card here...
            ID = p_CardID;
            Type = p_CardType;
 
            CurrentOrder = ID; 

            //Text = Functions.GetDescription(Type);
            //Text = ""; // calc card text
            //Priority = ID * 10; // calc card priority

            Owner = -1;  // unowned
            PhasePlayed = -1; // unplayed
            Locked = false; // not locked
            Executed = false;
            Random = false;
            Priority = ID * 10;
        }

        public MoveCard(MoveCard p_Card, tCardType p_CardType)
            : this(99, p_CardType)
        {
            Owner = p_Card.Owner;
            PhasePlayed = p_Card.PhasePlayed;
            Locked = p_Card.Locked;
            Executed = p_Card.Executed;
            Priority = p_Card.Priority;
            Type = p_CardType;
        }

        public MoveCard(int p_CardID, int p_CardType = 0)
            : this(p_CardID, (tCardType)p_CardType)
        {
        }


        public int ID { get; set; }

        public int Priority { get; set; }

        public tCardType Type { get; set; }

        public string Text { get { return Type.ToString(); } }

        public bool Executed { get; set; }

        public bool Locked { get; set; }

        public bool Random { get; set; }

        public int Owner { get; set; }

        public int PhasePlayed { get; set; }

        public int CurrentOrder { get; set; }

        public override string ToString()
        {
            string output = "(" + ID.ToString() + ") " + Text ;
            if (Owner > -1) output += " Player:" + Owner.ToString();
            if (PhasePlayed > 0) output += " Phase:" + PhasePlayed.ToString();
            if (Locked) output += " [Locked]";

            return output;
        }

        
    }

}
