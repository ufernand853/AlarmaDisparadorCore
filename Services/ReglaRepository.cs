using AlarmaDisparadorCore.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Data.SqlClient;
using System.IO;

namespace AlarmaDisparadorCore.Services
{
    public class ReglaRepository
    {
        private readonly string _connectionString;

        public ReglaRepository()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        public void ActualizarRegla(ReglaAlarma regla)
        {
            const string sqlRule = @"UPDATE dbo.reglas_alarmas SET nombre = @Name, operador = @LogicOperator, mensaje = @Message, activo = @IsActive, enviar_correo = @SendEmail, email_destino = @EmailTo, intervalo_minutos = @IntervaloMinutos WHERE id_regla = @Id";

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(sqlRule, conn);
            cmd.Parameters.AddWithValue("@Id", regla.Id);
            cmd.Parameters.AddWithValue("@Name", regla.Nombre);
            cmd.Parameters.AddWithValue("@LogicOperator", (object?)regla.Operador ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Message", (object?)regla.Mensaje ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", regla.Activo);
            cmd.Parameters.AddWithValue("@SendEmail", regla.EnviarCorreo);
            cmd.Parameters.AddWithValue("@EmailTo", (object?)regla.EmailDestino ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IntervaloMinutos", regla.IntervaloMinutos);
            cmd.ExecuteNonQuery();
        }
    }
}
