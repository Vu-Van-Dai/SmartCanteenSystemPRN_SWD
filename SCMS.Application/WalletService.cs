// File: SCMS.Application/WalletService.cs
using Microsoft.EntityFrameworkCore;
using SCMS.Domain;
using SCMS.Domain.DTOs;
using SCMS.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SCMS.Application
{
    public class WalletService : IWalletService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserService _userService;
        private readonly NotificationService _notificationService;

        public WalletService(ApplicationDbContext context, UserService userService, NotificationService notificationService)
        {
            _context = context;
            _userService = userService;
            _notificationService = notificationService;
        }

        private async Task<Wallet> GetOrCreateWalletAsync(int userId)
        {
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
            {
                wallet = new Wallet { UserId = userId, Balance = 0 };
                _context.Wallets.Add(wallet);
                await _context.SaveChangesAsync();
            }
            return wallet;
        }

        public async Task<WalletDto> GetWalletByUserIdAsync(int userId)
        {
            var wallet = await GetOrCreateWalletAsync(userId);
            return new WalletDto { Balance = wallet.Balance };
        }

        public async Task<List<TransactionDetailsDto>> GetTransactionHistoryAsync(int userId)
        {
            var wallet = await GetOrCreateWalletAsync(userId);
            return await _context.Transactions
                .Where(t => t.WalletId == wallet.WalletId)
                .OrderByDescending(t => t.TransactionDate)
                .Select(t => new TransactionDetailsDto
                {
                    TransactionId = t.TransactionId,
                    Type = t.Type.ToString(),
                    Amount = t.Amount,
                    Status = t.Status,
                    TransactionDate = t.TransactionDate,
                    Description = t.Description,
                    OrderId = t.OrderId
                })
                .ToListAsync();
        }

        public async Task<bool> TopUpAsync(int userId, decimal amount, int? fromUserId = null)
        {
            if (amount <= 0) return false;

            var wallet = await GetOrCreateWalletAsync(userId);
            wallet.Balance += amount;

            var description = fromUserId.HasValue
                ? $"Nạp tiền từ tài khoản phụ huynh (ID: {fromUserId.Value})"
                : "Tự nạp tiền.";

            _context.Transactions.Add(new Transaction
            {
                WalletId = wallet.WalletId,
                Type = TransactionType.TopUp,
                Amount = amount,
                Status = "Success",
                TransactionDate = DateTime.UtcNow,
                Description = description
            });

            var success = await _context.SaveChangesAsync() > 0;
            if (success)
            {
                await _notificationService.CreateNotificationAsync(userId, $"Bạn đã nạp thành công {amount:N0}đ vào ví.", "/wallet");
            }
            return success;
        }

        
    }
}