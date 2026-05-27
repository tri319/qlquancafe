using System.ComponentModel.DataAnnotations;

namespace QLquancafe.Models
{
    public class Customer
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Số điện thoại")]
        public string PhoneNumber { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Họ và tên")]
        public string? FullName { get; set; }

        [Display(Name = "Điểm hiện tại")]
        public int Points { get; set; } = 0;

        [Display(Name = "Điểm tích lũy trọn đời")]
        public int AccumulatedPoints { get; set; } = 0;

        [Required]
        [StringLength(50)]
        [Display(Name = "Hạng thành viên")]
        public string Tier { get; set; } = "Bronze"; // Bronze, Silver, Gold, Platinum

        [Display(Name = "Ngày tạo")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Quan hệ 1 khách hàng có nhiều đơn hàng
        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}
