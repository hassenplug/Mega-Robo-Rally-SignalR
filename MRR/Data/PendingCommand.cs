using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MRR.Data.Entities
{
    [Table("CommandList")]
    public class PendingCommandEntity
    {
        [Key]
        public int CommandID { get; set; }
        public int RobotID { get; set; }
        public int CommandTypeID { get; set; }
        public int StatusID { get; set; }
        public string? BTCommand { get; set; }
//        public string? BTReply { get; set; }
        public string? Description { get; set; }
        public int Turn { get; set; }
        public int Phase { get; set; }
        public int CommandSequence { get; set; }
        public int CommandSubSequence { get; set; }
        public int Parameter { get; set; }
        public int ParameterB { get; set; }
        public int PositionRow { get; set; }
        public int PositionCol { get; set; }
        public int PositionDir { get; set; }
        public int CommandCatID { get; set; }
    }
}