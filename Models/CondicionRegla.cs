namespace AlarmaDisparadorCore.Models
{
    public class CondicionRegla
    {
        public int Id { get; set; }
        public int IdRegla { get; set; }
        public int IdValor { get; set; }
        public string Operador { get; set; }
        public string Valor { get; set; }
    }
}