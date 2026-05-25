using System.ComponentModel.DataAnnotations;

namespace QLquancafe.Models
{
    public class Table
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Tên bàn")]
        public string TableName { get; set; } // Ví dụ: Bàn số 1, Bàn số 2

        [Required]
        [Display(Name = "Mã bàn")]
        public string TableCode { get; set; } // Mã để khách nhập vào (Ví dụ: BAN01, BAN02)

        [Display(Name = "Trạng thái")]
        public string Status { get; set; } = "Trống"; // Trống / Có khách
    }
}