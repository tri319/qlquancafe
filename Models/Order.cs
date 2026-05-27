using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QLquancafe.Models
{
    public class Order
    {
        [Key]
        public int Id { get; set; }

        // Thay đổi quan trọng: Thêm TableId để làm khóa ngoại kết nối với bảng Table
        [Required]
        [Display(Name = "Bàn")]
        public int TableId { get; set; }

        // Bạn có thể giữ TableNumber nếu muốn hiển thị số bàn nhanh, 
        // nhưng TableId mới là cái dùng để liên kết dữ liệu
        public int TableNumber { get; set; }

        [Display(Name = "Ngày đặt")]
        public DateTime OrderDate { get; set; } = DateTime.Now;

        [Display(Name = "Tổng tiền")]
        public decimal TotalAmount { get; set; }

        [Display(Name = "Trạng thái thanh toán")]
        public bool IsPaid { get; set; } = false;

        [Display(Name = "Trạng thái pha chế")]
        public string Status { get; set; } = "Pending"; // Pending, Preparing, Ready, Completed

        [Display(Name = "Phương thức thanh toán")]
        public string PaymentMethod { get; set; } = "Tiền mặt"; // Tiền mặt, VNPAY
        // --- CÁC TRƯỜNG THÀNH VIÊN & TÍCH ĐIỂM ---
        [Display(Name = "Mã thành viên")]
        public int? CustomerId { get; set; }

        [Display(Name = "Điểm tích lũy nhận được")]
        public int PointsEarned { get; set; } = 0;

        [Display(Name = "Điểm tích lũy đã tiêu dùng")]
        public int PointsRedeemed { get; set; } = 0;

        [Display(Name = "Số tiền giảm giá")]
        public decimal DiscountAmount { get; set; } = 0;

        [ForeignKey("CustomerId")]
        public virtual Customer? Customer { get; set; }
        // ----------------------------------------

        // Quan hệ 1 đơn hàng có nhiều chi tiết món ăn
        public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

        // Navigation property (Tùy chọn: giúp bạn dễ dàng truy cập thông tin bàn từ đơn hàng)
        [ForeignKey("TableId")]
        public virtual Table? Table { get; set; }
    }
}