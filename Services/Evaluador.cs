using AlarmaDisparadorCore.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AlarmaDisparadorCore.Services
{
    public class Evaluador
    {
        private readonly string _connectionString;

        public Evaluador()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        public void EvaluarReglas()
        {
            // Reglas
            List<ReglaAlarma> reglas = ObtenerReglas();

            // Valores actuales indexados por id_valor (SMALLINT -> short)
            Dictionary<short, ValorActual> valores = ObtenerValoresActuales();

            foreach (var regla in reglas.Where(r => r.Activo))
            {
                var condiciones = ObtenerCondicionesPorRegla(regla.Id);

                bool cumpleTodas = condiciones.All(cond =>
                {
                    // Si no existe el valor para la condición -> no cumple
                    if (!valores.ContainsKey(cond.IdValor))
                        return false;

                    var actual = valores[cond.IdValor];
                    return Comparar(actual, cond.Operador, cond.Valor);
                });

                if (cumpleTodas)
                {
                    if (DebeDisparar(regla))
                    {
                        Console.WriteLine($"Regla '{regla.Nombre}' disparada: {regla.Mensaje}");
                        LogDisparo(regla);
                    }

                    if (!regla.EnCurso)
                    {
                        regla.EnCurso = true;
                        ActualizarEnCursoRegla(regla);
                    }
                }
                else if (regla.EnCurso)
                {
                    regla.EnCurso = false;
                    ActualizarEnCursoRegla(regla);
                }
            }
        }

        /// <summary>
        /// Compara el valor actual (tipado) contra el literal 'valor' según el operador.
        /// Tipos:
        /// 1 = entero, 2 = decimal, 3 = string, 5 = bit
        /// </summary>
        private bool Comparar(ValorActual actual, string operador, string valor)
        {
            try
            {
                switch (actual.Tipo)
                {
                    case 1: // entero
                        {
                            int esperado = int.Parse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture);
                            int actualInt = Convert.ToInt32(actual.Valor);
                            return operador switch
                            {
                                "==" => actualInt == esperado,
                                "!=" => actualInt != esperado,
                                ">" => actualInt > esperado,
                                ">=" => actualInt >= esperado,
                                "<" => actualInt < esperado,
                                "<=" => actualInt <= esperado,
                                _ => false
                            };
                        }

                    case 2: // decimal
                        {
                            double esperado = double.Parse(valor, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                            double actualDec = Convert.ToDouble(actual.Valor, CultureInfo.InvariantCulture);
                            return operador switch
                            {
                                "==" => actualDec == esperado,
                                "!=" => actualDec != esperado,
                                ">" => actualDec > esperado,
                                ">=" => actualDec >= esperado,
                                "<" => actualDec < esperado,
                                "<=" => actualDec <= esperado,
                                _ => false
                            };
                        }

                    case 3: // string
                        {
                            string actualStr = actual.Valor?.ToString() ?? string.Empty;
                            return operador switch
                            {
                                "==" => string.Equals(actualStr, valor, StringComparison.Ordinal),
                                "!=" => !string.Equals(actualStr, valor, StringComparison.Ordinal),
                                _ => false
                            };
                        }

                    case 5: // bit
                        {
                            // permitimos "0"/"1" o "true"/"false"
                            bool esperado = valor is "1" or "true" or "True";
                            bool actualBit = actual.Valor switch
                            {
                                bool b => b,
                                int i => i != 0,
                                short s => s != 0,
                                _ => Convert.ToBoolean(actual.Valor)
                            };

                            return operador switch
                            {
                                "==" => actualBit == esperado,
                                "!=" => actualBit != esperado,
                                _ => false
                            };
                        }

                    default:
                        return false;
                }
            }
            catch
            {
                // Cualquier error de parseo o conversión -> condición no cumple
                return false;
            }
        }
        private static string DbValueToString(SqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return "";
            object v = reader.GetValue(ordinal);

            return v switch
            {
                string s => s,
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                short s16 => s16.ToString(CultureInfo.InvariantCulture),
                byte b => b.ToString(CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                double d => d.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString(CultureInfo.InvariantCulture),
                bool bb => bb ? "1" : "0",
                DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
            };
        }

        private List<ReglaAlarma> ObtenerReglas()
        {
            var reglas = new List<ReglaAlarma>();

            using var conn = new SqlConnection(_connectionString);
            try
            {
                conn.Open();
                using var cmd = new SqlCommand("SELECT id_regla, nombre, operador, mensaje, activo, en_curso, enviar_correo, email_destino, intervalo_minutos FROM reglas_alarma", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    reglas.Add(new ReglaAlarma
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1),
                        Operador = reader.GetString(2),
                        Mensaje = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Activo = reader.GetBoolean(4),
                        EnCurso = reader.IsDBNull(5) ? false : reader.GetBoolean(5),
                        EnviarCorreo = reader.IsDBNull(6) ? false : reader.GetBoolean(6),
                        EmailDestino = reader.IsDBNull(7) ? null : reader.GetString(7),
                        IntervaloMinutos = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8))
                    });
                }
            }
            catch
            {
                if (conn.State == ConnectionState.Open)
                    conn.Close();
                throw;
            }
            return reglas;
        }

        private List<CondicionRegla> ObtenerCondicionesPorRegla(int idRegla)
        {
            var condiciones = new List<CondicionRegla>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(
                "SELECT id_condicion, id_regla, id_valor, operador, valor FROM condiciones_regla WHERE id_regla = @id",
                conn);
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = idRegla;

            using var reader = cmd.ExecuteReader();

            int iId = reader.GetOrdinal("id_condicion");
            int iIdRegla = reader.GetOrdinal("id_regla");
            int iIdValor = reader.GetOrdinal("id_valor");
            int iOperador = reader.GetOrdinal("operador");
            int iValor = reader.GetOrdinal("valor");

            while (reader.Read())
            {
                condiciones.Add(new CondicionRegla
                {
                    Id = reader.GetInt32(iId),            // INT
                    IdRegla = reader.GetInt32(iIdRegla),       // INT
                    IdValor = reader.GetInt16(iIdValor),       // SMALLINT -> short
                    Operador = reader.IsDBNull(iOperador) ? "" : reader.GetString(iOperador),
                    Valor = DbValueToString(reader, iValor)  // << en vez de GetString(iValor)
                });
            }

            return condiciones;
        }

        private Dictionary<short, ValorActual> ObtenerValoresActuales()
        {
            var valores = new Dictionary<short, ValorActual>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            const string sql = @"
                        SELECT id_valor, id_tipovalor, 
                               valor_entero, valor_decimal, valor_string, valor_binario, valor_bit
                        FROM dbo.valores";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            int iId = reader.GetOrdinal("id_valor");      // smallint
            int iTipo = reader.GetOrdinal("id_tipovalor");  // tinyint
            int iEntero = reader.GetOrdinal("valor_entero");  // int
            int iDecimal = reader.GetOrdinal("valor_decimal"); // decimal(18,4)
            int iString = reader.GetOrdinal("valor_string");  // varchar(500)
            int iBinario = reader.GetOrdinal("valor_binario"); // binary(500)
            int iBit = reader.GetOrdinal("valor_bit");     // bit

            while (reader.Read())
            {
                short id = reader.GetInt16(iId);       // SMALLINT -> short
                byte tipo = reader.GetByte(iTipo);      // TINYINT  -> byte

                object data = null;

                switch (tipo)
                {
                    case 1: // entero
                        data = reader.IsDBNull(iEntero) ? 0 : reader.GetInt32(iEntero);
                        break;

                    case 2: // decimal
                        if (!reader.IsDBNull(iDecimal))
                        {
                            // guardamos double para simplificar comparaciones
                            data = Convert.ToDouble(reader.GetDecimal(iDecimal), System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else data = 0.0;
                        break;

                    case 3: // string
                        data = reader.IsDBNull(iString) ? string.Empty : reader.GetString(iString);
                        break;

                    case 4: // binario (si lo usás)
                        data = reader.IsDBNull(iBinario) ? Array.Empty<byte>() : (byte[])reader[iBinario];
                        break;

                    case 5: // bit
                        data = !reader.IsDBNull(iBit) && reader.GetBoolean(iBit);
                        break;

                    default:
                        // tipo no soportado: lo ignoramos
                        break;
                }

                if (data != null)
                {
                    valores[id] = new ValorActual
                    {
                        Id = id,    // short
                        Tipo = tipo,  // 1,2,3,4,5
                        Valor = data
                    };
                }
            }

            return valores;
        }

        private bool DebeDisparar(ReglaAlarma regla)
        {
            if (!regla.EnCurso)
                return true;

            if (regla.IntervaloMinutos <= 0)
                return false;

            var ultimo = ObtenerUltimoDisparo(regla.Id);
            if (ultimo == null)
                return true;

            return (DateTime.Now - ultimo.Value).TotalMinutes >= regla.IntervaloMinutos;
        }

        private DateTime? ObtenerUltimoDisparo(int idRegla)
        {
            using var conn = new SqlConnection(_connectionString);
            try
            {
                conn.Open();
                using var cmd = new SqlCommand("SELECT TOP 1 timestamp FROM disparos_alarmas WHERE id_regla = @id ORDER BY timestamp DESC", conn);
                cmd.Parameters.AddWithValue("@id", idRegla);
                var result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    return null;
                return Convert.ToDateTime(result);
            }
            catch
            {
                if (conn.State == ConnectionState.Open)
                    conn.Close();
                throw;
            }
        }

        private void ActualizarEnCursoRegla(ReglaAlarma regla)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(
                "UPDATE reglas_alarma SET en_curso = @enCurso WHERE id_regla = @id",
                conn);

            cmd.Parameters.Add("@enCurso", SqlDbType.Bit).Value = regla.EnCurso;
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = regla.Id;

            cmd.ExecuteNonQuery();
        }

        private void LogDisparo(ReglaAlarma regla)
        {
            using var conn = new SqlConnection(_connectionString);
            try
            {
                conn.Open();
                using var cmd = new SqlCommand("INSERT INTO disparos_alarmas (id_regla, mensaje, timestamp) VALUES (@id, @msg, @ts)", conn);
                cmd.Parameters.AddWithValue("@id", regla.Id);
                cmd.Parameters.AddWithValue("@msg", regla.Mensaje ?? "");
                cmd.Parameters.AddWithValue("@ts", DateTime.Now);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                if (conn.State == ConnectionState.Open)
                    conn.Close();
                throw;
            }
        }
    }
}
