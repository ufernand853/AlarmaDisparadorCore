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
        private readonly HashSet<int> _reglasProcesadas = new();
        private readonly Dictionary<int, DateTime> _inicioCumplimiento = new();
        private readonly EmailService _emailService;

        public Evaluador()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            _connectionString = config.GetConnectionString("DefaultConnection");
            _emailService = new EmailService(config);
        }

        public void EvaluarReglas()
        {
            try
            {
                // Reglas
                List<ReglaAlarma> reglas = ObtenerReglas();

                // Valores actuales indexados por id_valor
                Dictionary<int, ValorActual> valores = ObtenerValoresActuales();

                foreach (var regla in reglas.Where(r => r.Activo))
                {
                    var condiciones = ObtenerCondicionesPorRegla(regla.Id);
                    var evaluaciones = EvaluarCondiciones(condiciones, valores);

                    bool cumpleTodas = evaluaciones.All(ev => ev.Resultado);

                    if (cumpleTodas)
                    {
                        var ahora = DateTime.Now;
                        if (!_inicioCumplimiento.ContainsKey(regla.Id))
                        {
                            _inicioCumplimiento[regla.Id] = ahora;
                        }

                        bool tiempoCumplido = (ahora - _inicioCumplimiento[regla.Id]).TotalMinutes >= regla.IntervaloMinutos;

                        if (tiempoCumplido)
                        {
                            bool primeraEjecucionEnCurso = regla.EnCurso && !_reglasProcesadas.Contains(regla.Id);
                            bool reglaMarcadaEnCurso = regla.EnCurso;

                            if (!primeraEjecucionEnCurso && DebeDisparar(regla))
                            {
                                if (!reglaMarcadaEnCurso)
                                {
                                    reglaMarcadaEnCurso = IntentarMarcarEnCurso(regla);
                                }

                                if (reglaMarcadaEnCurso)
                                {
                                    RegistrarDetalleCondiciones(regla, evaluaciones);
                                    Logger.Log($"Regla '{regla.Nombre}' disparada: {regla.Mensaje}");
                                    LogDisparo(regla);
                                    _emailService.EnviarCorreo(regla);
                                }
                            }

                            if (!reglaMarcadaEnCurso)
                            {
                                reglaMarcadaEnCurso = IntentarMarcarEnCurso(regla);
                            }

                            if (reglaMarcadaEnCurso)
                            {
                                _reglasProcesadas.Add(regla.Id);
                            }
                        }
                    }
                    else
                    {
                        _inicioCumplimiento.Remove(regla.Id);

                        if (regla.EnCurso)
                        {
                            regla.EnCurso = false;
                            ActualizarEnCursoRegla(regla);
                        }
                        _reglasProcesadas.Remove(regla.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, nameof(EvaluarReglas));
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
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    $"{nameof(Comparar)} - operador: '{operador}', valor esperado: '{valor}', valor actual: '{actual?.Valor}', tipo: '{actual?.Tipo}'");

                // Cualquier error de parseo o conversión -> condición no cumple
                return false;
            }
        }

        private List<EvaluacionCondicion> EvaluarCondiciones(IEnumerable<CondicionRegla> condiciones, Dictionary<int, ValorActual> valores)
        {
            var resultados = new List<EvaluacionCondicion>();

            foreach (var condicion in condiciones)
            {
                if (!valores.TryGetValue(condicion.IdValor, out var valorActual))
                {
                    resultados.Add(EvaluacionCondicion.SinValor(condicion));
                    continue;
                }

                bool cumple = Comparar(valorActual, condicion.Operador, condicion.Valor);
                resultados.Add(EvaluacionCondicion.ConResultado(condicion, valorActual, cumple));
            }

            return resultados;
        }

        private void RegistrarDetalleCondiciones(ReglaAlarma regla, IReadOnlyCollection<EvaluacionCondicion> evaluaciones)
        {
            if (evaluaciones.Count == 0)
            {
                Logger.Log($"  Regla '{regla.Nombre}' sin condiciones asociadas.");
                return;
            }

            Logger.Log($"  Detalle de condiciones para '{regla.Nombre}':");
            foreach (var evaluacion in evaluaciones)
            {
                Logger.Log($"    {FormatearDetalleCondicion(evaluacion)}");
            }
        }

        private string FormatearDetalleCondicion(EvaluacionCondicion evaluacion)
        {
            var condicion = evaluacion.Condicion;
            string valorEsperado = condicion.Valor ?? "(null)";
            string valorActual = evaluacion.ValorActual != null ? FormatearValorActual(evaluacion.ValorActual) : "(sin valor)";
            string estado = evaluacion.Resultado ? "OK" : "NO CUMPLE";

            if (!string.IsNullOrEmpty(evaluacion.Observacion))
            {
                estado = $"{estado} - {evaluacion.Observacion}";
            }

            return $"[{condicion.IdValor}] {valorActual} {condicion.Operador} {valorEsperado} => {estado}";
        }

        private string FormatearValorActual(ValorActual actual)
        {
            return actual.Tipo switch
            {
                1 => Convert.ToInt32(actual.Valor).ToString(CultureInfo.InvariantCulture),
                2 => Convert.ToDouble(actual.Valor, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                3 => actual.Valor?.ToString() ?? string.Empty,
                4 => actual.Valor is byte[] bytes ? BitConverter.ToString(bytes).Replace("-", string.Empty) : "(binario)",
                5 => (actual.Valor is bool b ? b : Convert.ToBoolean(actual.Valor)).ToString(),
                _ => actual.Valor?.ToString() ?? string.Empty
            };
        }

        private sealed class EvaluacionCondicion
        {
            private EvaluacionCondicion(CondicionRegla condicion, ValorActual? valorActual, bool resultado, string? observacion)
            {
                Condicion = condicion;
                ValorActual = valorActual;
                Resultado = resultado;
                Observacion = observacion;
            }

            public CondicionRegla Condicion { get; }
            public ValorActual? ValorActual { get; }
            public bool Resultado { get; }
            public string? Observacion { get; }

            public static EvaluacionCondicion SinValor(CondicionRegla condicion) =>
                new(condicion, null, false, "valor no disponible");

            public static EvaluacionCondicion ConResultado(CondicionRegla condicion, ValorActual valorActual, bool resultado) =>
                new(condicion, valorActual, resultado, null);
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
            catch (Exception ex)
            {
                Logger.LogError(ex, nameof(ObtenerReglas));
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

        private Dictionary<int, ValorActual> ObtenerValoresActuales()
        {
            var valores = new Dictionary<int, ValorActual>();

            using var conn = new SqlConnection(_connectionString);
            try
            {
                conn.Open();

                const string sql = @"
                        SELECT val.id_valor, val.id_tipovalor,
                                val.valor_entero, val.valor_decimal, val.valor_string, val.valor_binario, val.valor_bit
                        FROM dbo.variables v
                        JOIN dbo.valores val ON v.id_valor = val.id_valor";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();

            int iId = reader.GetOrdinal("id_valor");        // id_valor (clave)
            int iTipo = reader.GetOrdinal("id_tipovalor");  // tinyint
            int iEntero = reader.GetOrdinal("valor_entero");  // int
            int iDecimal = reader.GetOrdinal("valor_decimal"); // decimal(18,4)
            int iString = reader.GetOrdinal("valor_string");  // varchar(500)
            int iBinario = reader.GetOrdinal("valor_binario"); // binary(500)
            int iBit = reader.GetOrdinal("valor_bit");     // bit

                while (reader.Read())
                {
                    int idValor = Convert.ToInt32(reader.GetValue(iId));
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
                        valores[idValor] = new ValorActual
                        {
                            Id = idValor,
                            Tipo = tipo,  // 1,2,3,4,5
                            Valor = data
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, nameof(ObtenerValoresActuales));
                if (conn.State == ConnectionState.Open)
                    conn.Close();
                throw;
            }

            return valores;
        }

        private bool DebeDisparar(ReglaAlarma regla)
        {
            // Si la regla ya está en curso no se debe disparar nuevamente hasta que
            // las condiciones dejen de cumplirse y se reinicie el estado.
            if (regla.EnCurso)
                return false;

            // Si no hay restricción de intervalo se permite el disparo inmediato
            if (regla.IntervaloMinutos <= 0)
                return true;

            // Evitamos disparar si ya existe un disparo registrado dentro del intervalo
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
            catch (Exception ex)
            {
                Logger.LogError(ex, nameof(ObtenerUltimoDisparo));
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

        private bool IntentarMarcarEnCurso(ReglaAlarma regla)
        {
            if (regla.EnCurso)
            {
                return true;
            }

            using var conn = new SqlConnection(_connectionString);
            try
            {
                conn.Open();

                using var cmd = new SqlCommand(
                    "UPDATE reglas_alarma SET en_curso = 1 WHERE id_regla = @id AND (en_curso IS NULL OR en_curso = 0)",
                    conn);

                cmd.Parameters.Add("@id", SqlDbType.Int).Value = regla.Id;

                int filasAfectadas = cmd.ExecuteNonQuery();
                if (filasAfectadas > 0)
                {
                    regla.EnCurso = true;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, nameof(IntentarMarcarEnCurso));
                if (conn.State == ConnectionState.Open)
                    conn.Close();
                throw;
            }
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
            catch (Exception ex)
            {
                Logger.LogError(ex, nameof(LogDisparo));
                if (conn.State == ConnectionState.Open)
                    conn.Close();
                throw;
            }
        }
    }
}
