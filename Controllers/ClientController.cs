using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using QLquancafe.Data;
using QLquancafe.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System;

namespace QLquancafe.Controllers
{
    public class ClientController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ClientController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Trang nhập mã bàn
        public IActionResult LoginTable()
        {
            return View();
        }

        // Xử lý khi bấm nút "Vào bàn"
        [HttpPost]
        public async Task<IActionResult> AccessByTable(string tableCode)
        {
            if (string.IsNullOrWhiteSpace(tableCode))
            {
                ModelState.AddModelError("", "Vui lòng nhập mã bàn!");
                return View("LoginTable");
            }

            // Chuẩn hóa chuỗi nhập để loại bỏ khoảng trắng dư thừa
            tableCode = tableCode.Trim();

            // Kiểm tra mã bàn có khớp trong DB không
            var table = await _context.Tables.FirstOrDefaultAsync(t => t.TableCode == tableCode);

            if (table != null)
            {
                // Lưu ID bàn và Tên bàn vào Session
                HttpContext.Session.SetInt32("CurrentTableId", table.Id);
                HttpContext.Session.SetString("CurrentTableName", table.TableName);

                // Đổi trạng thái bàn thành "Có khách"
                table.Status = "Có khách";
                await _context.SaveChangesAsync();

                // Chuyển hướng thẳng sang trang gọi món cho bàn này
                return RedirectToAction("Create", "Orders", new { tableId = table.Id });
            }

            ModelState.AddModelError("", "Mã bàn không đúng hoặc không tồn tại!");
            return View("LoginTable");
        }

        // Thoát bàn (Khách tính tiền hoặc ra về)
        public async Task<IActionResult> LeaveTable()
        {
            var tableId = HttpContext.Session.GetInt32("CurrentTableId");
            if (tableId.HasValue)
            {
                var table = await _context.Tables.FindAsync(tableId.Value);
                if (table != null)
                {
                    table.Status = "Trống";
                    await _context.SaveChangesAsync();
                }
            }
            
            // Xóa Session sau khi xong
            HttpContext.Session.Clear();
            return RedirectToAction("LoginTable");
        }

        // --- CÁC AJAX ENDPOINTS CHO HỆ THỐNG THÀNH VIÊN ---

        // Liên kết số điện thoại thành viên (Chỉ cần Số điện thoại)
        [HttpPost]
        public async Task<IActionResult> LinkMember(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return Json(new { success = false, message = "Vui lòng nhập số điện thoại!" });
            }

            phone = phone.Trim();

            // Tìm kiếm khách hàng theo số điện thoại
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == phone);

            if (customer == null)
            {
                return Json(new { success = false, message = "Số điện thoại này chưa được đăng ký thành viên! Vui lòng chọn tab 'Đăng ký mới' bên dưới để mở tài khoản." });
            }

            // Lưu thông tin thành viên vào Session
            HttpContext.Session.SetInt32("LinkedMemberId", customer.Id);
            HttpContext.Session.SetString("LinkedMemberPhone", customer.PhoneNumber);
            HttpContext.Session.SetString("LinkedMemberName", customer.FullName ?? "Khách hàng");
            HttpContext.Session.SetInt32("LinkedMemberPoints", customer.Points);
            HttpContext.Session.SetString("LinkedMemberTier", customer.Tier);
            HttpContext.Session.SetInt32("LinkedMemberAccumulatedPoints", customer.AccumulatedPoints);
            
            // Reset số điểm muốn đổi khi liên kết mới
            HttpContext.Session.Remove("RedeemedPoints");

            // Tính toán mốc điểm lên hạng kế tiếp
            int nextTierTarget = 100;
            string nextTierName = "Bạc";
            if (customer.Tier == "Silver") { nextTierTarget = 300; nextTierName = "Vàng"; }
            else if (customer.Tier == "Gold") { nextTierTarget = 1000; nextTierName = "Kim Cương"; }
            else if (customer.Tier == "Platinum") { nextTierTarget = 0; nextTierName = "Tối đa"; }

            return Json(new {
                success = true,
                id = customer.Id,
                phone = customer.PhoneNumber,
                name = customer.FullName,
                points = customer.Points,
                accumulatedPoints = customer.AccumulatedPoints,
                tier = customer.Tier,
                nextTierTarget = nextTierTarget,
                nextTierName = nextTierName
            });
        }

        // Đăng ký mới thành viên (Cần cả Số điện thoại & Họ tên)
        [HttpPost]
        public async Task<IActionResult> RegisterMember(string phone, string fullName)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return Json(new { success = false, message = "Vui lòng nhập số điện thoại!" });
            }
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return Json(new { success = false, message = "Vui lòng nhập Họ & Tên để đăng ký!" });
            }

            phone = phone.Trim();
            fullName = fullName.Trim();

            // Kiểm tra xem số điện thoại đã được sử dụng chưa
            var existingCustomer = await _context.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == phone);
            if (existingCustomer != null)
            {
                return Json(new { success = false, message = "Số điện thoại này đã được đăng ký thành viên trước đó! Vui lòng chọn tab 'Đã có tài khoản' để liên kết thẻ." });
            }

            // Tạo mới tài khoản khách hàng
            var customer = new Customer
            {
                PhoneNumber = phone,
                FullName = fullName,
                Points = 0,
                AccumulatedPoints = 0,
                Tier = "Bronze",
                CreatedAt = DateTime.Now
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            // Lưu thông tin thành viên vào Session
            HttpContext.Session.SetInt32("LinkedMemberId", customer.Id);
            HttpContext.Session.SetString("LinkedMemberPhone", customer.PhoneNumber);
            HttpContext.Session.SetString("LinkedMemberName", customer.FullName);
            HttpContext.Session.SetInt32("LinkedMemberPoints", customer.Points);
            HttpContext.Session.SetString("LinkedMemberTier", customer.Tier);
            HttpContext.Session.SetInt32("LinkedMemberAccumulatedPoints", customer.AccumulatedPoints);
            
            // Reset số điểm muốn đổi
            HttpContext.Session.Remove("RedeemedPoints");

            return Json(new {
                success = true,
                id = customer.Id,
                phone = customer.PhoneNumber,
                name = customer.FullName,
                points = customer.Points,
                accumulatedPoints = customer.AccumulatedPoints,
                tier = customer.Tier,
                nextTierTarget = 100,
                nextTierName = "Bạc"
            });
        }

        // Hủy liên kết thành viên khỏi phiên gọi món hiện tại
        [HttpPost]
        public IActionResult UnlinkMember()
        {
            HttpContext.Session.Remove("LinkedMemberId");
            HttpContext.Session.Remove("LinkedMemberPhone");
            HttpContext.Session.Remove("LinkedMemberName");
            HttpContext.Session.Remove("LinkedMemberPoints");
            HttpContext.Session.Remove("LinkedMemberTier");
            HttpContext.Session.Remove("LinkedMemberAccumulatedPoints");
            HttpContext.Session.Remove("RedeemedPoints");

            return Json(new { success = true });
        }

        // Áp dụng số điểm tích lũy muốn đổi để giảm giá
        [HttpPost]
        public async Task<IActionResult> ApplyPoints(int points)
        {
            var memberId = HttpContext.Session.GetInt32("LinkedMemberId");
            if (!memberId.HasValue)
            {
                return Json(new { success = false, message = "Bạn chưa liên kết tài khoản thành viên!" });
            }

            var customer = await _context.Customers.FindAsync(memberId.Value);
            if (customer == null)
            {
                return Json(new { success = false, message = "Tài khoản thành viên không tồn tại!" });
            }

            if (points < 0)
            {
                return Json(new { success = false, message = "Số điểm nhập vào không hợp lệ!" });
            }

            if (points > customer.Points)
            {
                return Json(new { success = false, message = "Bạn không có đủ điểm! Điểm hiện tại: " + customer.Points });
            }

            // Lưu số điểm muốn quy đổi vào Session
            HttpContext.Session.SetInt32("RedeemedPoints", points);

            return Json(new { 
                success = true, 
                redeemedPoints = points, 
                discountAmount = points * 1000 
            });
        }
    }
}