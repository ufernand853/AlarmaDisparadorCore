namespace AlarmaDisparadorCore.Models
{
    public class ReglaAlarma
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Operador { get; set; }
        public string Mensaje { get; set; }
        public bool Activo { get; set; }
    }
}