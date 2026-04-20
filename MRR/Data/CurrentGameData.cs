using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MRR.Data.Entities
{
    [Table("CurrentGameData")]
    public class CurrentGameDataEntity
    {
        [Key]
        [Column("iKey")]
        public int IKey { get; set; }

        [Column("sKey")]
        public string? SKey { get; set; }

        [Column("iValue")]
        public int IValue { get; set; }

        [Column("sValue")]
        public string? SValue { get; set; }
    }
}
