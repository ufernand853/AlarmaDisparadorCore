using System;

namespace AlarmaDisparadorCore.Models
{
    public class ReglaAlarma
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Operador { get; set; }
        public string Mensaje { get; set; }
        public bool Activo { get; set; }
        public bool EnCurso { get; set; } = false;
        public bool EnviarCorreo { get; set; }
        public string EmailDestino { get; set; }
        public int IntervaloMinuto { get; set; }
    }
}