using AlarmaDisparadorCore.Models;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System.Data;

namespace AlarmaDisparadorCore.Services
{
    public class Evaluador
    {
        private string _connectionString;

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
            List<ReglaAlarma> reglas = ObtenerReglas();
            Dictionary<int, ValorActual> valores = ObtenerValoresActuales();

            foreach (var regla in reglas.Where(r => r.Activo))
            {
                var condiciones = ObtenerCondicionesPorRegla(regla.Id);

                bool cumpleTodas = condiciones.All(cond =>
                {
                    if (!valores.ContainsKey(cond.IdValor))
                        return false;

                    var actual = valores[cond.IdValor];
                    return Comparar(actual, cond.Operador, cond.Valor);
                });

                if (cumpleTodas)
                {
                    Console.WriteLine($"Alarma disparada: {regla.Mensaje}");
                    LogDisparo(regla);
                }
            }
        }

        private bool Comparar(ValorActual actual, string operador, string valor)
        {
            try
            {
                switch (actual.Tipo)
                {
                    case 1: // entero
                        int vInt = Convert.ToInt32(valor);
                        int actualInt = Convert.ToInt32(actual.Valor);
                        return operador switch
                        {
                            "==" => actualInt == vInt,
                            "!=" => actualInt != vInt,
                            ">" => actualInt > vInt,
                            "<" => actualInt < vInt,
                            _ => false
                        };
                    case 2: // decimal
                        double vDec = Convert.ToDouble(valor);
                        double actualDec = Convert.ToDouble(actual.Valor);
                        return operador switch
                        {
                            "==" => actualDec == vDec,
                            "!=" => actualDec != vDec,
                            ">" => actualDec > vDec,
                            "<" => actualDec < vDec,
                            _ => false
                        };
                    case 3: // string
                        return operador switch
                        {
                            "==" => actual.Valor.ToString() == valor,
                            "!=" => actual.Valor.ToString() != valor,
                            _ => false
                        };
                    case 5: // bit
                        bool vBit = Convert.ToBoolean(Convert.ToInt32(valor));
                        bool actualBit = Convert.ToBoolean(Convert.ToInt32(actual.Valor));
                        return operador switch
                        {
                            "==" => actualBit == vBit,
                            "!=" => actualBit != vBit,
                            _ => false
                        };
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private List<ReglaAlarma> ObtenerReglas()
        {
            var reglas = new List<ReglaAlarma>();
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT id_regla, nombre, operador, mensaje, activo FROM reglas_alarma", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                reglas.Add(new ReglaAlarma
                {
                    Id = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    Operador = reader.GetString(2),
                    Mensaje = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Activo = reader.GetBoolean(4)
                });
            }
            return reglas;
        }

        private List<CondicionRegla> ObtenerCondicionesPorRegla(int idRegla)
        {
            var condiciones = new List<CondicionRegla>();
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT id_condicion, id_regla, id_valor, operador, valor FROM condiciones_regla WHERE id_regla = @id", conn);
            cmd.Parameters.AddWithValue("@id", idRegla);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                condiciones.Add(new CondicionRegla
                {
                    Id = reader.GetInt32(0),
                    IdRegla = reader.GetInt32(1),
                    IdValor = reader.GetInt32(2),
                    Operador = reader.GetString(3),
                    Valor = reader.GetString(4)
                });
            }
            return condiciones;
        }

        private Dictionary<int, ValorActual> ObtenerValoresActuales()
        {
            var valores = new Dictionary<int, ValorActual>();
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT id_valor, id_tipovalor, valor_entero, valor_decimal, valor_string, valor_bit FROM valores", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                int tipo = reader.GetInt32(1);
                object valor = tipo switch
                {
                    1 => reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    2 => reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3),
                    3 => reader.IsDBNull(4) ? "" : reader.GetString(4),
                    5 => reader.IsDBNull(5) ? 0 : reader.GetBoolean(5) ? 1 : 0,
                    _ => null
                };

                if (valor != null)
                {
                    valores[id] = new ValorActual
                    {
                        Id = id,
                        Tipo = tipo,
                        Valor = valor
                    };
                }
            }
            return valores;
        }

        private void LogDisparo(ReglaAlarma regla)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("INSERT INTO DisparosAlarmas (id_regla, mensaje, timestamp) VALUES (@id, @msg, @ts)", conn);
            cmd.Parameters.AddWithValue("@id", regla.Id);
            cmd.Parameters.AddWithValue("@msg", regla.Mensaje ?? "");
            cmd.Parameters.AddWithValue("@ts", DateTime.Now);
            cmd.ExecuteNonQuery();
        }
    }
}