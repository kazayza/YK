using Microsoft.Data.SqlClient;

namespace YKCoatings.Services
{
    public abstract class BaseDbService
    {
        private readonly string _connectionString;

        protected BaseDbService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new Exception("Connection string 'DefaultConnection' غير موجودة");
        }

        protected SqlConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}