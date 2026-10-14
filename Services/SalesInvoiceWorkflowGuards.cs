using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public partial class SalesInvoiceService
    {
        private const string RejectionNoteMarker = "\n[[YK-SINV-REJECTION]]\n";

        private async Task<ModulePermissionDto> GetCurrentInvoicePermissionsAsync(int userId)
        {
            if (userId <= 0 || !_userSession.IsLoggedIn || _userSession.UserID != userId)
                throw new UnauthorizedAccessException("جلسة المستخدم غير صالحة؛ سجّل الدخول مرة أخرى.");

            // Refresh on every financial write/read so a permission change is not hidden by stale circuit state.
            await _permissionService.LoadPermissionsAsync(userId);
            return _permissionService.GetModule("SCR_SINV")
                ?? throw new UnauthorizedAccessException("لا توجد صلاحيات مهيأة لفواتير المبيعات.");
        }

        private async Task RequireInvoicePermissionAsync(
            int userId,
            Func<ModulePermissionDto, bool> hasPermission,
            string message)
        {
            var permissions = await GetCurrentInvoicePermissionsAsync(userId);
            if (!hasPermission(permissions))
                throw new UnauthorizedAccessException(message);
        }

        private async Task RequireViewAndPrintAsync(int userId)
        {
            var permissions = await GetCurrentInvoicePermissionsAsync(userId);
            if (!permissions.CanView || !permissions.CanPrint)
                throw new UnauthorizedAccessException("لا تملك صلاحية عرض وطباعة فواتير المبيعات.");
        }

        private async Task RequireViewAndExportAsync(int userId)
        {
            var permissions = await GetCurrentInvoicePermissionsAsync(userId);
            if (!permissions.CanView || !permissions.CanExport)
                throw new UnauthorizedAccessException("لا تملك صلاحية عرض وتصدير فواتير المبيعات.");
        }

        private async Task EnsureCanEditDraftAsync(
            IDbConnection connection,
            IDbTransaction? transaction,
            int invoiceId,
            int userId,
            ModulePermissionDto? permissions = null)
        {
            permissions ??= await GetCurrentInvoicePermissionsAsync(userId);
            if (permissions.CanEdit) return;

            // CanAdd also permits the creator to finish their own draft, but not edit another user's draft.
            if (permissions.CanAdd)
            {
                var ownsDraft = await connection.QueryFirstOrDefaultAsync<int>(
                    @"SELECT CASE WHEN EXISTS
                      (SELECT 1 FROM dbo.SalesInvoices
                       WHERE InvoiceID=@InvoiceID AND CreatedBy=@UserID AND InvoiceStatus=1)
                      THEN 1 ELSE 0 END",
                    new { InvoiceID = invoiceId, UserID = userId }, transaction);
                if (ownsDraft == 1) return;
            }

            throw new UnauthorizedAccessException("لا تملك صلاحية تعديل هذه المسودة.");
        }

        private static (string? Notes, string? RejectionReason) SplitRejectionNote(string? storedNotes)
        {
            if (string.IsNullOrEmpty(storedNotes)) return (storedNotes, null);

            var markerIndex = storedNotes.LastIndexOf(RejectionNoteMarker, StringComparison.Ordinal);
            if (markerIndex < 0) return (storedNotes, null);

            var notes = storedNotes[..markerIndex].TrimEnd('\r', '\n');
            var reason = storedNotes[(markerIndex + RejectionNoteMarker.Length)..].Trim();
            return (string.IsNullOrWhiteSpace(notes) ? null : notes,
                    string.IsNullOrWhiteSpace(reason) ? null : reason);
        }

        private static string? CombineNotesAndRejection(string? notes, string? rejectionReason)
        {
            var cleanNotes = SplitRejectionNote(notes).Notes;
            if (string.IsNullOrWhiteSpace(rejectionReason)) return cleanNotes;

            return string.IsNullOrWhiteSpace(cleanNotes)
                ? RejectionNoteMarker + rejectionReason.Trim()
                : cleanNotes.TrimEnd() + RejectionNoteMarker + rejectionReason.Trim();
        }

        private static async Task RecalculateSalesInvoiceDueDateAsync(
            SqlConnection connection,
            IDbTransaction? transaction,
            int invoiceId)
        {
            var affected = await connection.ExecuteAsync(
                @"UPDATE si
                  SET DueDate=DATEADD(DAY, ISNULL(pt.DueDays,0), CAST(si.InvoiceDate AS DATE))
                  FROM dbo.SalesInvoices si
                  LEFT JOIN dbo.PaymentTerms pt ON pt.PaymentTermID=si.PaymentTermID
                  WHERE si.InvoiceID=@InvoiceID",
                new { InvoiceID = invoiceId }, transaction);
            if (affected != 1)
                throw new InvalidOperationException("تعذر احتساب تاريخ استحقاق فاتورة المبيعات.");
        }

    }
}
