using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QLquancafe.Data;
using QLquancafe.Models;
using Microsoft.AspNetCore.Authorization;

namespace QLquancafe.Controllers
{
    [Authorize(Policy = "RequireStaffRole")]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public OrdersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. Hiển thị danh sách hóa đơn và thống kê doanh thu
        public async Task<IActionResult> Index(string filter = "today")
        {
            var query = _context.Orders
                .Include(o => o.Table)
                .AsQueryable();

            DateTime today = DateTime.Today;

            if (filter == "today")
            {
                query = query.Where(o => o.OrderDate.Date == today);
            }
            else if (filter == "week")
            {
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                DateTime startOfWeek = today.AddDays(-1 * diff).Date;
                query = query.Where(o => o.OrderDate.Date >= startOfWeek);
            }
            else if (filter == "month")
            {
                DateTime startOfMonth = new DateTime(today.Year, today.Month, 1);
                query = query.Where(o => o.OrderDate.Date >= startOfMonth);
            }

            var orders = await query.OrderByDescending(o => o.OrderDate).ToListAsync();

            // Tính toán thống kê
            ViewBag.TotalRevenue = orders.Where(o => o.IsPaid).Sum(o => o.TotalAmount);
            ViewBag.CompletedOrdersCount = orders.Count(o => o.IsPaid);
            ViewBag.PendingOrdersCount = orders.Count(o => !o.IsPaid);
            ViewBag.CurrentFilter = filter;

            return View(orders);
        }

        // 2. Mở bàn và chọn món (Giao diện Menu cho khách/nhân viên)
        [AllowAnonymous]
        public async Task<IActionResult> Create(int tableId)
        {
            // Kiểm tra xem bàn có tồn tại không
            var table = await _context.Tables.FindAsync(tableId);
            if (table == null) return NotFound("Bàn không tồn tại.");

            var products = await _context.Products.Include(p => p.Category).ToListAsync();

            ViewBag.TableId = tableId;
            ViewBag.TableName = table.TableName;
            return View(products);
        }

        // Chuyển hướng đến hóa đơn đang mở của bàn (Dành cho nhân viên xem chi tiết/thanh toán)
        [AllowAnonymous]
        public async Task<IActionResult> ActiveOrder(int tableId)
        {
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.TableId == tableId && !o.IsPaid);

            if (order != null)
            {
                return RedirectToAction(nameof(Details), new { id = order.Id });
            }
            
            // Nếu không có Order đang mở, quay về trang chủ hoặc mở bàn mới
            return RedirectToAction("Index", "Home");
        }

        // 3. Xử lý lưu hóa đơn và gọi thêm món (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Checkout([FromForm] int tableId, [FromForm] int[] productIds, [FromForm] int[] quantities, [FromForm] string[] sugarLevels, [FromForm] string[] iceLevels, [FromForm] string[] sizes)
        {
            if (productIds == null || productIds.Length == 0 || quantities == null || !quantities.Any(q => q > 0))
            {
                TempData["ErrorMessage"] = "Vui lòng chọn ít nhất 1 món để gọi!";
                var sessionTableId = HttpContext.Session.GetInt32("CurrentTableId");
                if (sessionTableId.HasValue) return RedirectToAction("Create", new { tableId = tableId });
                return RedirectToAction("Create", new { tableId = tableId }); // Trở lại phần mở bàn để gọi món
            }

            // [LOGIC MỚI] Tìm hóa đơn chưa thanh toán của bàn này thay vì luôn tạo mới
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.TableId == tableId && !o.IsPaid);

            bool isNewOrder = false;
            if (order == null)
            {
                // Mở bàn mới
                order = new Order
                {
                    TableId = tableId,
                    TableNumber = tableId,
                    OrderDate = DateTime.Now,
                    IsPaid = false,
                    TotalAmount = 0,
                    OrderDetails = new List<OrderDetail>()
                };
                isNewOrder = true;
            }

            // Duyệt danh sách món khách chọn
            for (int i = 0; i < productIds.Length; i++)
            {
                if (quantities[i] > 0)
                {
                    var product = await _context.Products.FindAsync(productIds[i]);
                    if (product != null)
                    {
                        string sugar = (sugarLevels != null && sugarLevels.Length > i) ? sugarLevels[i] : "100%";
                        string ice = (iceLevels != null && iceLevels.Length > i) ? iceLevels[i] : "100%";
                        string size = (sizes != null && sizes.Length > i) ? sizes[i] : "M";

                        decimal finalPrice = product.Price;
                        if (size == "L")
                        {
                            finalPrice += 10000;
                        }

                        var existingDetail = order.OrderDetails.FirstOrDefault(d => 
                            d.ProductId == productIds[i] && 
                            d.SugarLevel == sugar && 
                            d.IceLevel == ice &&
                            d.Size == size);

                        if (existingDetail != null)
                        {
                            // Cộng dồn số lượng nếu đã gọi món này trước đó
                            existingDetail.Quantity += quantities[i];
                        }
                        else
                        {
                            // Thêm món mới
                            var detail = new OrderDetail
                            {
                                ProductId = productIds[i],
                                Quantity = quantities[i],
                                UnitPrice = finalPrice,
                                SugarLevel = sugar,
                                IceLevel = ice,
                                Size = size
                            };
                            order.OrderDetails.Add(detail);
                        }
                    }
                }
            }

            if (order.OrderDetails.Count > 0)
            {
                // Cập nhật lại tổng tiền
                order.TotalAmount = order.OrderDetails.Sum(d => d.Quantity * d.UnitPrice);

                if (isNewOrder)
                {
                    _context.Orders.Add(order);
                }
                else
                {
                    _context.Orders.Update(order);
                }

                // Cập nhật trạng thái bàn thành có khách
                var table = await _context.Tables.FindAsync(tableId);
                if (table != null)
                {
                    table.Status = "Có khách";
                }

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Details), new { id = order.Id });
            }

            var sTableId = HttpContext.Session.GetInt32("CurrentTableId");
            if (sTableId.HasValue) return RedirectToAction("Create", new { tableId = tableId });
            return RedirectToAction("Index", "Home");
        }

        // 4. Xem chi tiết hóa đơn (Dùng để kiểm tra món và tính tiền)
        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                 .Include(o => o.Table)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (order == null) return NotFound();

            return View(order);
        }

        // 5. Thanh toán và Giải phóng bàn (Chuyển màu đỏ sang xanh)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                order.IsPaid = true; // Đánh dấu đã trả tiền
                _context.Update(order);

                // Cập nhật trạng thái bàn về "Trống"
                var table = await _context.Tables.FindAsync(order.TableId);
                if (table != null)
                {
                    table.Status = "Trống";
                }

                await _context.SaveChangesAsync();
            }

            // Quay về trang chủ để cập nhật trạng thái bàn trên sơ đồ
            return RedirectToAction("Index", "Home");
        }

        // 6. Xóa hóa đơn (Hủy bàn khi khách đổi ý hoặc bấm nhầm)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order != null)
            {
                // Xóa các chi tiết trước nếu DB không cấu hình Cascade Delete
                _context.OrderDetails.RemoveRange(order.OrderDetails);

                // Cập nhật trạng thái bàn về "Trống"
                var table = await _context.Tables.FindAsync(order.TableId);
                if (table != null)
                {
                    table.Status = "Trống";
                }

                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Index", "Home");
        }
    }
}