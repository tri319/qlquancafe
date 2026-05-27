using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QLquancafe.Data;
using QLquancafe.Models;
using Microsoft.AspNetCore.SignalR;
using QLquancafe.Hubs;
using System.Threading.Tasks;
using System.Linq;
using System;
using Microsoft.AspNetCore.Authorization;

namespace QLquancafe.Controllers
{
    [Authorize(Policy = "RequireStaffRole")]
    public class KitchenController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public KitchenController(ApplicationDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // Màn hình Kanban Nhà bếp / Quầy Bar
        public async Task<IActionResult> Index()
        {
            var activeOrders = await _context.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                .Where(o => !o.IsPaid && o.Status != "Completed")
                .OrderBy(o => o.OrderDate)
                .ToListAsync();

            return View(activeOrders);
        }

        // AJAX API: Cập nhật trạng thái pha chế đơn hàng
        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, string status)
        {
            var order = await _context.Orders
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return Json(new { success = false, message = "Đơn hàng không tồn tại!" });
            }

            // Chuẩn hóa và lưu trạng thái mới
            status = status.Trim();
            if (status != "Pending" && status != "Preparing" && status != "Ready" && status != "Completed")
            {
                return Json(new { success = false, message = "Trạng thái không hợp lệ!" });
            }

            order.Status = status;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            // Phát sự kiện SignalR cập nhật trạng thái đơn hàng thời gian thực
            try
            {
                await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, status);

                // Nếu nước uống đã làm xong (Ready) -> Gửi thông báo cho nhân viên chạy bàn mang phục vụ khách
                if (status == "Ready")
                {
                    string tableName = order.Table?.TableName ?? ("Bàn " + order.TableId);
                    await _hubContext.Clients.All.SendAsync("ReceiveWaiterNotification", order.Id, tableName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SignalR Kitchen error: " + ex.Message);
            }

            return Json(new { success = true, id = order.Id, status = order.Status });
        }
    }
}
