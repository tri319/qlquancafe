using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using QLquancafe.Data; // Thay bằng namespace DbContext của bạn
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

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

            // Kiểm tra mã bàn có khớp trong DB không (Sử dụng Async/Await để tối ưu)
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
    }
}