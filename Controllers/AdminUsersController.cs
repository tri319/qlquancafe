using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QLquancafe.Models.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QLquancafe.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminUsersController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;

        public AdminUsersController(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        // GET: AdminUsers
        public async Task<IActionResult> Index()
        {
            // Lấy tất cả user thuộc role Staff
            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");

            var model = new List<AdminUserViewModel>();
            foreach (var user in staffUsers)
            {
                model.Add(new AdminUserViewModel
                {
                    Id = user.Id,
                    Email = user.Email,
                    Role = "Staff"
                });
            }

            return View(model);
        }

        // GET: AdminUsers/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: AdminUsers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateStaffViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new IdentityUser { UserName = model.Email, Email = model.Email, EmailConfirmed = true };
                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    // Gắn role Staff cho user này
                    await _userManager.AddToRoleAsync(user, "Staff");
                    return RedirectToAction(nameof(Index));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        // GET: AdminUsers/Delete/5
        public async Task<IActionResult> Delete(string id)
        {
            if (id == null) return NotFound();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var isInStaffRole = await _userManager.IsInRoleAsync(user, "Staff");
            if (!isInStaffRole)
            {
                // Để bảo mật, chỉ cho xoá những người thuộc role Staff từ trang này
                return RedirectToAction(nameof(Index));
            }

            var model = new AdminUserViewModel
            {
                Id = user.Id,
                Email = user.Email,
                Role = "Staff"
            };

            return View(model);
        }

        // POST: AdminUsers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                // Có thể kiểm tra lại xem có phải tự xoá mình/xóa admin khác không
                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                if (!isAdmin)
                {
                    await _userManager.DeleteAsync(user);
                }
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
