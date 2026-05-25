using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QLquancafe.Models;

namespace QLquancafe.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
            // --- CÁCH 2: ÉP EF TẠO LẠI DATABASE ---
            // Lệnh này sẽ tự động kiểm tra và tạo lại toàn bộ bảng dựa trên các DbSet bên dưới.
            // Nó cực kỳ hữu ích khi bạn không thể chạy lệnh Migration do bị Windows chặn.
            Database.EnsureCreated();
        }

        // 1. Quản lý danh mục và món ăn
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }

        // 2. Quản lý bàn (Dùng để hiển thị sơ đồ và đăng nhập tại bàn)
        public DbSet<Table> Tables { get; set; }

        // 3. Quản lý bán hàng
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Quan trọng: Phải giữ dòng này để cấu hình các bảng Identity (User, Role)
            base.OnModelCreating(modelBuilder);

            // --- Cấu hình định dạng tiền tệ (Decimal) ---
            modelBuilder.Entity<Product>()
                .Property(p => p.Price)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Order>()
                .Property(o => o.TotalAmount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<OrderDetail>()
                .Property(od => od.UnitPrice)
                .HasColumnType("decimal(18,2)");

            // --- Cấu hình ràng buộc bảng Table ---
            modelBuilder.Entity<Table>()
                .HasIndex(t => t.TableCode)
                .IsUnique(); // Mã bàn (BAN01, BAN02...) không được trùng nhau

            // --- CẤU HÌNH QUAN HỆ CHUẨN ĐỂ XÓA TableId1 ---
            modelBuilder.Entity<Order>()
                .HasOne(o => o.Table)           // Thuộc tính Table trong class Order
                .WithMany()                     // Một bàn có nhiều hóa đơn
                .HasForeignKey(o => o.TableId)  // Khóa ngoại là TableId
                .OnDelete(DeleteBehavior.Restrict); // Không cho xóa bàn nếu đang có hóa đơn
        }
    }
}