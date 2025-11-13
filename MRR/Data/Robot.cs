using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MRR.Data
{
    [Table("Robots")]
    public class Robot
    {
        [Key]
        public int RobotID { get; set; }
        
        public int? MessageCommandID { get; set; }
        
        // Add other properties as needed
    }
}