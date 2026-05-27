using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QLquancafe.Data;
using QLquancafe.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace QLquancafe.Controllers
{
    [Authorize(Policy = "RequireStaffRole")]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<QLquancafe.Hubs.NotificationHub> _hubContext;

        public OrdersController(ApplicationDbContext context, Microsoft.AspNetCore.SignalR.IHubContext<QLquancafe.Hubs.NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // 1. Hiển thị danh sách hóa đơn và thống kê doanh thu (ApexCharts Analytics Dashboard)
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

            // Tính toán thống kê cơ bản
            var paidOrders = orders.Where(o => o.IsPaid).ToList();
            ViewBag.TotalRevenue = paidOrders.Sum(o => o.TotalAmount);
            ViewBag.CompletedOrdersCount = paidOrders.Count;
            ViewBag.PendingOrdersCount = orders.Count(o => !o.IsPaid);
            ViewBag.CurrentFilter = filter;

            // Phân loại Doanh Thu: Tiền mặt vs VNPAY
            ViewBag.CashRevenue = paidOrders.Where(o => o.PaymentMethod != "VNPAY").Sum(o => o.TotalAmount);
            ViewBag.VnPayRevenue = paidOrders.Where(o => o.PaymentMethod == "VNPAY").Sum(o => o.TotalAmount);

            // --- GOM NHÓM DOANH THU THEO TRỤC THỜI GIAN (LINE CHART) ---
            var chartLabels = new List<string>();
            var chartValues = new List<decimal>();

            if (filter == "today")
            {
                for (int h = 6; h <= 23; h++) // Thời gian hoạt động: 6h sáng đến 23h đêm
                {
                    chartLabels.Add($"{h:D2}:00");
                    var sum = paidOrders.Where(o => o.OrderDate.Hour == h).Sum(o => o.TotalAmount);
                    chartValues.Add(sum);
                }
            }
            else if (filter == "week")
            {
                string[] dayNames = { "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7", "Chủ Nhật" };
                int[] dotNetDays = { 1, 2, 3, 4, 5, 6, 0 }; // Sunday = 0, Monday = 1...
                for (int i = 0; i < 7; i++)
                {
                    chartLabels.Add(dayNames[i]);
                    var sum = paidOrders.Where(o => (int)o.OrderDate.DayOfWeek == dotNetDays[i]).Sum(o => o.TotalAmount);
                    chartValues.Add(sum);
                }
            }
            else if (filter == "month")
            {
                int maxDays = DateTime.DaysInMonth(today.Year, today.Month);
                for (int d = 1; d <= maxDays; d++)
                {
                    chartLabels.Add($"N{d:D2}");
                    var sum = paidOrders.Where(o => o.OrderDate.Day == d && o.OrderDate.Month == today.Month && o.OrderDate.Year == today.Year).Sum(o => o.TotalAmount);
                    chartValues.Add(sum);
                }
            }
            else // all
            {
                for (int m = 1; m <= 12; m++)
                {
                    chartLabels.Add($"Tháng {m}");
                    var sum = paidOrders.Where(o => o.OrderDate.Month == m && o.OrderDate.Year == today.Year).Sum(o => o.TotalAmount);
                    chartValues.Add(sum);
                }
            }

            ViewBag.ChartLabelsJson = System.Text.Json.JsonSerializer.Serialize(chartLabels);
            ViewBag.ChartValuesJson = System.Text.Json.JsonSerializer.Serialize(chartValues);

            // --- GOM NHÓM TOP 5 THỨC UỐNG BÁN CHẠY NHẤT (DONUT CHART) ---
            var paidOrderIds = paidOrders.Select(o => o.Id).ToList();
            var bestSellersQuery = await _context.OrderDetails
                .Include(od => od.Product)
                .Where(od => paidOrderIds.Contains(od.OrderId))
                .GroupBy(od => od.Product != null ? od.Product.Name : "Món nước khác")
                .Select(g => new { ProductName = g.Key, TotalQty = g.Sum(od => od.Quantity) })
                .OrderByDescending(x => x.TotalQty)
                .Take(5)
                .ToListAsync();

            var bestSellerNames = bestSellersQuery.Select(x => x.ProductName).ToList();
            var bestSellerQtys = bestSellersQuery.Select(x => x.TotalQty).ToList();

            ViewBag.BestSellerNamesJson = System.Text.Json.JsonSerializer.Serialize(bestSellerNames);
            ViewBag.BestSellerQtysJson = System.Text.Json.JsonSerializer.Serialize(bestSellerQtys);

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
                // Cập nhật lại tổng tiền gốc
                decimal originalTotal = order.OrderDetails.Sum(d => d.Quantity * d.UnitPrice);

                // --- TÍCH HỢP HỆ THỐNG THÀNH VIÊN ---
                var memberId = HttpContext.Session.GetInt32("LinkedMemberId");
                var redeemedPoints = HttpContext.Session.GetInt32("RedeemedPoints") ?? 0;

                if (memberId.HasValue)
                {
                    var customer = await _context.Customers.FindAsync(memberId.Value);
                    if (customer != null)
                    {
                        order.CustomerId = memberId.Value;

                        // Đảm bảo số điểm dùng không lớn hơn số điểm thực tế khách có
                        if (redeemedPoints > customer.Points)
                        {
                            redeemedPoints = customer.Points;
                        }

                        order.PointsRedeemed = redeemedPoints;
                        order.DiscountAmount = redeemedPoints * 1000;
                        order.TotalAmount = Math.Max(0, originalTotal - order.DiscountAmount);

                        // Tính toán điểm tích lũy mới nhận được tạm tính (10.000 đ = 1 điểm)
                        int basePoints = (int)Math.Floor(order.TotalAmount / 10000);
                        
                        // Nhân thêm ưu đãi hạng thành viên
                        double multiplier = 1.0;
                        if (customer.Tier == "Silver") multiplier = 1.05;
                        else if (customer.Tier == "Gold") multiplier = 1.10;
                        else if (customer.Tier == "Platinum") multiplier = 1.15;

                        order.PointsEarned = (int)Math.Floor(basePoints * multiplier);
                    }
                    else
                    {
                        order.CustomerId = null;
                        order.PointsRedeemed = 0;
                        order.DiscountAmount = 0;
                        order.PointsEarned = 0;
                        order.TotalAmount = originalTotal;
                    }
                }
                else
                {
                    order.CustomerId = null;
                    order.PointsRedeemed = 0;
                    order.DiscountAmount = 0;
                    order.PointsEarned = 0;
                    order.TotalAmount = originalTotal;
                }
                // ------------------------------------

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

                // Phát tín hiệu SignalR đơn đặt món mới thời gian thực
                try
                {
                    string tableNameStr = table?.TableName ?? ("Bàn " + tableId);
                    await _hubContext.Clients.All.SendAsync("ReceiveNewOrder", order.Id, tableNameStr);
                }
                catch (System.Exception) { /* Lờ đi nếu có lỗi socket */ }

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
            var order = await _context.Orders
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == id);
                
            if (order != null && !order.IsPaid)
            {
                order.IsPaid = true; // Đánh dấu đã trả tiền
                _context.Update(order);

                // --- CỘNG/TRỪ ĐIỂM TÍCH LŨY THÀNH VIÊN KHI THANH TOÁN THÀNH CÔNG ---
                if (order.CustomerId.HasValue)
                {
                    var customer = await _context.Customers.FindAsync(order.CustomerId.Value);
                    if (customer != null)
                    {
                        // Trừ điểm đã tiêu dùng để giảm giá
                        customer.Points = Math.Max(0, customer.Points - order.PointsRedeemed);

                        // Cộng điểm thưởng tích lũy mới
                        customer.Points += order.PointsEarned;
                        customer.AccumulatedPoints += order.PointsEarned;

                        // Tự động kiểm tra và thăng hạng thành viên
                        if (customer.AccumulatedPoints >= 1000)
                        {
                            customer.Tier = "Platinum";
                        }
                        else if (customer.AccumulatedPoints >= 300)
                        {
                            customer.Tier = "Gold";
                        }
                        else if (customer.AccumulatedPoints >= 100)
                        {
                            customer.Tier = "Silver";
                        }
                        else
                        {
                            customer.Tier = "Bronze";
                        }

                        _context.Customers.Update(customer);
                    }
                }
                // ----------------------------------------------------------------

                // Cập nhật trạng thái bàn về "Trống"
                var table = await _context.Tables.FindAsync(order.TableId);
                if (table != null)
                {
                    table.Status = "Trống";
                }

                await _context.SaveChangesAsync();

                // [KẾT NỐI REAL-TIME] Gửi tín hiệu thông báo đã thanh toán qua SignalR tới tất cả Client.
                // Khi khách hàng đang mở trang Details.cshtml (xem hóa đơn), trình duyệt của họ sẽ 
                // nhận được sự kiện này, hiển thị một thông báo thành công đẹp mắt và tự động tải lại 
                // trang sau 1.5 giây để cập nhật trạng thái "ĐÃ THANH TOÁN" một cách đồng bộ.
                try
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveOrderPaid", order.Id);
                }
                catch (System.Exception) { /* Bỏ qua ngoại lệ nếu có sự cố về kết nối socket */ }

                // [XÓA DỮ LIỆU PHIÊN LÀM VIỆC] Giải phóng và dọn sạch Session liên quan đến thông tin 
                // thành viên của bàn này, đảm bảo lượt khách hàng tiếp theo mở bàn không bị trùng lặp thông tin cũ.
                HttpContext.Session.Remove("LinkedMemberId");
                HttpContext.Session.Remove("LinkedMemberPhone");
                HttpContext.Session.Remove("LinkedMemberName");
                HttpContext.Session.Remove("LinkedMemberPoints");
                HttpContext.Session.Remove("LinkedMemberTier");
                HttpContext.Session.Remove("LinkedMemberAccumulatedPoints");
                HttpContext.Session.Remove("RedeemedPoints");
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