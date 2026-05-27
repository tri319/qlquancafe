using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QLquancafe.Data;
using QLquancafe.Helpers;
using Microsoft.AspNetCore.SignalR;

namespace QLquancafe.Controllers
{
    /// <summary>
    /// Bộ điều khiển tích hợp cổng thanh toán điện tử (VNPAY) cho hệ thống quán cà phê.
    /// Quản lý việc tạo liên kết thanh toán an toàn và tiếp nhận phản hồi kết quả giao dịch thời gian thực.
    /// </summary>
    public class PaymentController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<QLquancafe.Hubs.NotificationHub> _hubContext;

        /// <summary>
        /// Hàm khởi tạo nạp các phụ thuộc cần thiết.
        /// </summary>
        /// <param name="configuration">Cấu hình hệ thống (appsettings.json)</param>
        /// <param name="context">Cơ sở dữ liệu Entity Framework Core</param>
        /// <param name="hubContext">Cổng kết nối thời gian thực SignalR</param>
        public PaymentController(IConfiguration configuration, ApplicationDbContext context, Microsoft.AspNetCore.SignalR.IHubContext<QLquancafe.Hubs.NotificationHub> hubContext)
        {
            _configuration = configuration;
            _context = context;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Khởi tạo yêu cầu thanh toán trực tuyến qua cổng VNPAY.
        /// Tạo lập đường dẫn thanh toán an toàn được ký bảo mật SHA256 và chuyển hướng khách hàng tới cổng giao dịch.
        /// </summary>
        /// <param name="orderId">Mã hóa đơn cần thực hiện thanh toán</param>
        public async Task<IActionResult> CreatePaymentUrl(int orderId)
        {
            // 1. Tìm đơn hàng tương ứng trong Cơ sở dữ liệu
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null || order.IsPaid)
            {
                // Nếu đơn hàng không tồn tại hoặc đã được thanh toán trước đó
                var sessionTableId = HttpContext.Session.GetInt32("CurrentTableId");
                if (sessionTableId.HasValue)
                {
                    // Nếu là khách hàng tại bàn, quay lại trang thực đơn gọi món
                    return RedirectToAction("Create", "Orders", new { tableId = sessionTableId.Value });
                }
                // Nếu không có thông tin bàn, quay về trang chủ sơ đồ bàn
                return RedirectToAction("Index", "Home");
            }

            // 2. Nạp cấu hình tham số bảo mật của VNPAY từ tệp tin cấu hình appsettings.json
            var url = _configuration["Vnpay:BaseUrl"];
            var returnUrl = _configuration["Vnpay:ReturnUrl"];
            var tmnCode = _configuration["Vnpay:TmnCode"];
            var hashSecret = _configuration["Vnpay:HashSecret"];

            // 3. Khởi tạo đối tượng VnPayLibrary để đóng gói dữ liệu yêu cầu giao dịch
            VnPayLibrary pay = new VnPayLibrary();

            // Nạp các tham số bắt buộc của tài liệu API VNPAY phiên bản 2.1.0
            pay.AddRequestData("vnp_Version", "2.1.0");
            pay.AddRequestData("vnp_Command", "pay");
            pay.AddRequestData("vnp_TmnCode", tmnCode);
            pay.AddRequestData("vnp_Amount", (order.TotalAmount * 100).ToString("0")); // Số tiền nhân 100 theo quy định VNPAY
            pay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
            pay.AddRequestData("vnp_CurrCode", "VND");
            pay.AddRequestData("vnp_IpAddr", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1");
            pay.AddRequestData("vnp_Locale", "vn");
            pay.AddRequestData("vnp_OrderInfo", "Thanh toan don hang: " + order.Id);
            pay.AddRequestData("vnp_OrderType", "other"); // Phân loại nhóm giao dịch mặc định
            pay.AddRequestData("vnp_ReturnUrl", returnUrl); // Trang callback mà VNPAY sẽ trả kết quả về sau khi thanh toán
            // Sử dụng mã giao dịch độc nhất kết hợp dấu ticks thời gian để tránh trùng lặp mã đơn trên cổng kiểm thử VNPAY
            pay.AddRequestData("vnp_TxnRef", order.Id.ToString() + "_" + DateTime.Now.Ticks.ToString()); 

            // 4. Sinh chuỗi URL thanh toán bảo mật bao gồm chữ ký số SHA256 (vnp_SecureHash)
            string paymentUrl = pay.CreateRequestUrl(url, hashSecret);

            // 5. Chuyển hướng trình duyệt của khách hàng sang trang thanh toán chính thức của ngân hàng/VNPAY
            return Redirect(paymentUrl);
        }

        /// <summary>
        /// Tiếp nhận phản hồi từ VNPAY (IPN / Return URL) sau khi khách hàng hoàn thành giao dịch trên cổng.
        /// Xác minh chữ ký số SHA256 để đảm bảo tính toàn vẹn của dữ liệu và cập nhật trạng thái đơn hàng thời gian thực.
        /// </summary>
        public async Task<IActionResult> PaymentCallback()
        {
            if (Request.Query.Count > 0)
            {
                string hashSecret = _configuration["Vnpay:HashSecret"]; 
                var vnpayData = Request.Query;
                VnPayLibrary pay = new VnPayLibrary();

                // Đọc toàn bộ tham số phản hồi trả về từ VNPAY
                foreach (var s in vnpayData)
                {
                    if (!string.IsNullOrEmpty(s.Key) && s.Key.StartsWith("vnp_"))
                    {
                        pay.AddResponseData(s.Key, s.Value.ToString());
                    }
                }

                // Chiết xuất các dữ liệu thanh toán quan trọng từ phản hồi
                string orderIdAndTicks = pay.GetResponseData("vnp_TxnRef");
                long vnp_Amount = Convert.ToInt64(pay.GetResponseData("vnp_Amount")) / 100;
                string vnp_ResponseCode = pay.GetResponseData("vnp_ResponseCode");
                string vnp_SecureHash = Request.Query["vnp_SecureHash"];

                // 1. Tiến hành xác minh tính hợp lệ của chữ ký phản hồi để tránh giả mạo dữ liệu (Tấn công chèn mã)
                bool checkSignature = pay.ValidateSignature(vnp_SecureHash, hashSecret);

                if (checkSignature)
                {
                    // Nếu chữ ký số hợp lệ và mã phản hồi bằng "00" (Đại diện cho Giao dịch thành công)
                    if (vnp_ResponseCode == "00")
                    {
                        // Phân tích mã hóa đơn thực tế từ TxnRef (Lấy phần trước dấu gạch dưới)
                        int orderId = int.Parse(orderIdAndTicks.Split('_')[0]);
                        var order = await _context.Orders
                            .Include(o => o.Customer)
                            .FirstOrDefaultAsync(o => o.Id == orderId);
                            
                        if (order != null && !order.IsPaid)
                        {
                            // Đánh dấu đơn hàng là đã thanh toán thành công và ghi nhận phương thức là VNPAY
                            order.IsPaid = true;
                            order.PaymentMethod = "VNPAY";
                            _context.Update(order);

                            // --- [HỆ THỐNG THÀNH VIÊN] CẬP NHẬT ĐIỂM TÍCH LŨY & THĂNG HẠNG THÀNH VIÊN ---
                            if (order.CustomerId.HasValue)
                            {
                                var customer = await _context.Customers.FindAsync(order.CustomerId.Value);
                                if (customer != null)
                                {
                                    // B1. Khấu trừ đi điểm tích lũy khách hàng đã đồng ý sử dụng để giảm giá hóa đơn này
                                    customer.Points = Math.Max(0, customer.Points - order.PointsRedeemed);

                                    // B2. Cộng thêm điểm thưởng tích lũy mới nhận được từ hóa đơn này
                                    customer.Points += order.PointsEarned;
                                    customer.AccumulatedPoints += order.PointsEarned; // Điểm tích lũy trọn đời làm căn cứ thăng hạng

                                    // B3. Tự động kiểm tra và thăng cấp bậc hạng thành viên dựa trên mốc điểm tích lũy trọn đời
                                    if (customer.AccumulatedPoints >= 1000)
                                    {
                                        customer.Tier = "Platinum"; // Hạng Bạch Kim: Ưu đãi tốt nhất
                                    }
                                    else if (customer.AccumulatedPoints >= 300)
                                    {
                                        customer.Tier = "Gold";     // Hạng Vàng
                                    }
                                    else if (customer.AccumulatedPoints >= 100)
                                    {
                                        customer.Tier = "Silver";   // Hạng Bạc
                                    }
                                    else
                                    {
                                        customer.Tier = "Bronze";   // Hạng Đồng mặc định
                                    }

                                    _context.Customers.Update(customer);
                                }
                            }
                            // ----------------------------------------------------------------------

                            // Giải phóng trạng thái bàn về mức "Trống" (Sẵn sàng tiếp đón lượt khách mới)
                            var table = await _context.Tables.FindAsync(order.TableId);
                            if (table != null)
                            {
                                table.Status = "Trống";
                            }

                            // Lưu toàn bộ thay đổi dữ liệu vào cơ sở dữ liệu hệ thống
                            await _context.SaveChangesAsync();

                            // [TÍNH NĂNG REAL-TIME] Phát tín hiệu thông báo thanh toán thành công qua cổng SignalR
                            // Tín hiệu này sẽ được gửi tới tất cả các thiết bị khách hàng đang xem chi tiết hóa đơn (Details.cshtml)
                            // giúp trang tự động làm mới (Reload) để cập nhật trạng thái mới nhất mà không cần bấm F5.
                            try
                            {
                                await _hubContext.Clients.All.SendAsync("ReceiveOrderPaid", order.Id);
                            }
                            catch (System.Exception) { /* Bỏ qua ngoại lệ nếu kết nối socket bị gián đoạn */ }

                            // [DỌN DẸP SESSION] Hủy bỏ toàn bộ thông tin lưu trữ trong Session liên quan đến 
                            // thành viên được liên kết với bàn này, nhằm chuẩn bị cho lượt khách tiếp theo sử dụng bàn trống.
                            HttpContext.Session.Remove("LinkedMemberId");
                            HttpContext.Session.Remove("LinkedMemberPhone");
                            HttpContext.Session.Remove("LinkedMemberName");
                            HttpContext.Session.Remove("LinkedMemberPoints");
                            HttpContext.Session.Remove("LinkedMemberTier");
                            HttpContext.Session.Remove("LinkedMemberAccumulatedPoints");
                            HttpContext.Session.Remove("RedeemedPoints");
                        }
                        ViewBag.Message = "Thanh toán thành công đơn hàng " + orderId;
                        return View("PaymentSuccess"); // Hiển thị màn hình thông báo thanh toán thành công
                    }
                    else
                    {
                        // Giao dịch không thành công tại phía VNPAY (Ví dụ: Khách chủ động hủy, thẻ không đủ số dư,...)
                        ViewBag.Message = "Có lỗi xảy ra trong quá trình xử lý giao dịch. Mã lỗi VNPAY: " + vnp_ResponseCode;
                        return View("PaymentFail");
                    }
                }
                else
                {
                    // Chữ ký bảo mật nhận lại không khớp với chữ ký khởi tạo (Có hành vi can thiệp sửa đổi gói tin)
                    ViewBag.Message = "Có lỗi xảy ra trong quá trình xử lý: Chữ ký bảo mật phản hồi không hợp lệ.";
                    return View("PaymentFail");
                }
            }

            // Phòng hờ các truy cập callback rỗng, tự động quay về trang thực đơn tương ứng
            var sessionTableId = HttpContext.Session.GetInt32("CurrentTableId");
            if (sessionTableId.HasValue)
            {
                return RedirectToAction("Create", "Orders", new { tableId = sessionTableId.Value });
            }
            return RedirectToAction("Index", "Home");
        }
    }
}
