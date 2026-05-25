using Microsoft.AspNetCore.Mvc;
using QLquancafe.Models;
using System.Diagnostics;
using QLquancafe.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace QLquancafe.Controllers
{
    [Authorize(Policy = "RequireStaffRole")]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Lấy danh sách ID của các bàn đang có hóa đơn chưa thanh toán
            var occupiedTableIds = await _context.Orders
                .Where(o => o.IsPaid == false)
                .Select(o => o.TableId)
                .Distinct()
                .ToListAsync();

            ViewBag.OccupiedTables = occupiedTableIds;

            // Đảm bảo trong DB có bàn để hiển thị, nếu chưa có thì tự động tạo 15 bàn mặc định
            if (!await _context.Tables.AnyAsync())
            {
                var defaultTables = new List<Table>();
                for (int i = 1; i <= 15; i++)
                {
                    defaultTables.Add(new Table { TableName = "Bàn " + i, TableCode = "BAN" + i, Status = "Trống" });
                }
                _context.Tables.AddRange(defaultTables);
                await _context.SaveChangesAsync();
            }

            // Lấy toàn bộ danh sách bàn thực sự trong Database truyền sang View
            var tables = await _context.Tables.ToListAsync();
            
            // Sắp xếp các bàn theo Tên (tùy chọn) để dễ hiển thị
            tables = tables.OrderBy(t => { 
                var num = t.TableName.Replace("Bàn", "").Replace("BAN", "").Trim();
                int idx = 0;
                int.TryParse(num, out idx);
                return idx;
            }).ToList();

            return View(tables);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
