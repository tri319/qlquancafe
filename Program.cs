using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QLquancafe.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình kết nối SQL Server Local Database
// Thiết lập DbContext sử dụng chuỗi kết nối "DefaultConnection" được khai báo trong appsettings.json.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Cấu hình hệ thống xác thực người dùng (ASP.NET Core Identity)
// Tùy chỉnh các ràng buộc về mật khẩu để thuận tiện trong quá trình kiểm thử và vận hành nội bộ (không yêu cầu ký tự đặc biệt, chữ hoa hay độ dài quá phức tạp).
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
    options.SignIn.RequireConfirmedAccount = false; // Không bắt buộc xác nhận email để kích hoạt tài khoản
    options.Password.RequireDigit = false;          // Không bắt buộc phải có chữ số
    options.Password.RequiredLength = 6;            // Độ dài mật khẩu tối thiểu là 6 ký tự
    options.Password.RequireNonAlphanumeric = false;// Không bắt buộc có ký tự đặc biệt (@, #, $,...)
    options.Password.RequireUppercase = false;      // Không bắt buộc chữ hoa
    options.Password.RequireLowercase = false;      // Không bắt buộc chữ thường
})
.AddRoles<IdentityRole>() // Kích hoạt cơ chế phân quyền theo vai trò (Role-based Authorization: Admin, Staff, Customer)
.AddEntityFrameworkStores<ApplicationDbContext>();

// 3. Cấu hình phân phối bộ nhớ đệm và Session (Phiên làm việc)
// Sử dụng Distributed Memory Cache để quản lý dữ liệu lưu trữ tạm thời cho từng bàn và thành viên.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromHours(4); // Thời gian hết hạn phiên làm việc tối đa là 4 tiếng kể từ lượt truy cập cuối cùng
    options.Cookie.HttpOnly = true;              // Bảo mật Cookie chống tấn công XSS (chỉ truy cập qua HTTP, không truy cập bằng JS client)
    options.Cookie.IsEssential = true;           // Đánh dấu là Cookie tối quan trọng cho trải nghiệm người dùng
});
builder.Services.AddHttpContextAccessor(); // Cho phép truy cập thông tin HttpContext (Session, Connection,...) trong các tầng dịch vụ khác nhau

// 4. Cấu hình Cookie cho hệ thống xác thực tài khoản
// Thiết lập đường dẫn trang đăng nhập, đăng xuất và trang thông báo từ chối truy cập khi không đủ quyền hạn.
builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = $"/Identity/Account/Login";
    options.LogoutPath = $"/Identity/Account/Logout";
    options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30); // Thời gian ghi nhớ trạng thái đăng nhập tối đa là 30 ngày
    options.SlidingExpiration = true;               // Tự động gia hạn thời gian đăng nhập khi người dùng liên tục tương tác với hệ thống
});

// 5. Cấu hình chính sách ủy quyền (Authorization Policies)
// Thiết lập hai mức phân quyền cơ bản: Chỉ dành cho Quản trị viên (Admin) và Dành cho cả Quản trị viên & Nhân viên (Admin + Staff).
builder.Services.AddAuthorization(options => {
    options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireStaffRole", policy => policy.RequireRole("Admin", "Staff"));
});

// 6. Đăng ký các dịch vụ MVC và SignalR thời gian thực
builder.Services.AddControllersWithViews(); // Đăng ký các Controller quản lý giao diện
builder.Services.AddRazorPages();           // Đăng ký Razor Pages hỗ trợ cho Identity
builder.Services.AddSignalR();             // Kích hoạt dịch vụ truyền dữ liệu thời gian thực SignalR

var app = builder.Build();

// 7. Thực hiện Seeding dữ liệu (Khởi tạo vai trò và tài khoản quản trị mặc định)
// Được chạy ngay khi ứng dụng khởi động thành công để đảm bảo cơ sở dữ liệu luôn sẵn sàng hoạt động.
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    // Khởi tạo 3 nhóm vai trò cơ bản nếu chưa tồn tại trong cơ sở dữ liệu
    string[] roleNames = { "Admin", "Staff", "Customer" };
    foreach (var roleName in roleNames)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }

    // Khởi tạo tài khoản Quản trị viên tối cao (Admin) mặc định
    var adminEmail = "admin@cafe.com";
    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        var user = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, "Admin123!");
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Admin");
        }
    }

    // Khởi tạo tài khoản Nhân viên phục vụ (Staff) mặc định
    var staffEmail = "nhanvien@cafe.com";
    var staffUser = await userManager.FindByEmailAsync(staffEmail);
    if (staffUser == null)
    {
        var user = new IdentityUser
        {
            UserName = staffEmail,
            Email = staffEmail,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, "Nhanvien123!");
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Staff");
        }
    }
}

// 8. Cấu hình Pipeline xử lý HTTP Request (Thứ tự gọi Middleware rất quan trọng)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error"); // Trang xử lý và hiển thị lỗi thân thiện với người dùng
    app.UseHsts();                          // Ép buộc kết nối an toàn HTTPS (HTTP Strict Transport Security)
}

app.UseHttpsRedirection(); // Tự động chuyển hướng từ HTTP thông thường sang HTTPS bảo mật
app.UseStaticFiles();      // Cho phép truy cập và tải các tài nguyên tĩnh trong wwwroot (CSS, JS, Hình ảnh, Font)
app.UseRouting();          // Định tuyến các yêu cầu HTTP tới các Controller tương ứng

// Kích hoạt Session (Thiết lập phiên làm việc bắt buộc phải đặt TRƯỚC Authentication để tránh mất mát dữ liệu phiên)
app.UseSession();

app.UseAuthentication(); // Middleware xác thực danh tính người dùng (Đăng nhập)
app.UseAuthorization();  // Middleware phân quyền truy cập tính năng (Quyền hạn)

app.MapRazorPages(); // Liên kết định tuyến cho các trang Identity mặc định

// Bản đồ hóa cổng kết nối SignalR thời gian thực để thiết bị khách và quầy bếp giao tiếp
app.MapHub<QLquancafe.Hubs.NotificationHub>("/notificationHub");

// Đăng ký định tuyến mặc định cho các Controller MVC
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run(); // Khởi động và lắng nghe các yêu cầu truy cập ứng dụng