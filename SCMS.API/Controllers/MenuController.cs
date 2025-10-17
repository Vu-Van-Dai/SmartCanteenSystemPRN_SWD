// File: SCMS.API/Controllers/MenuController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http; // THÊM using này
using Microsoft.AspNetCore.Mvc;
using SCMS.Application;
using SCMS.Domain.DTOs;
using System.Threading.Tasks;

namespace SCMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MenuController : ControllerBase
    {
        private readonly IMenuService _menuService;

        public MenuController(IMenuService menuService)
        {
            _menuService = menuService;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Get([FromQuery] string? searchTerm, [FromQuery] int? categoryId)
        {
            var menuItems = await _menuService.GetMenuItemsAsync(searchTerm, categoryId);
            return Ok(menuItems);
        }

        [HttpGet("categories")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _menuService.GetAllCategoriesAsync();
            return Ok(categories);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var menuItem = await _menuService.GetMenuItemByIdAsync(id);
            if (menuItem == null) return NotFound();
            return Ok(menuItem);
        }


        
    }
}