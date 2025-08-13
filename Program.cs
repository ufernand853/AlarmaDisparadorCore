using AlarmaDisparadorCore.Services;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();

var intervaloSegundos = 60;
if (int.TryParse(config["IntervaloSegundos"], out var parsedIntervalo))
{
    intervaloSegundos = parsedIntervalo;
}

var evaluador = new Evaluador();

while (true)
{
    Console.WriteLine($"Inicio de evaluación: {DateTime.Now:O}");
    evaluador.EvaluarReglas();
    Console.WriteLine($"Fin de evaluación: {DateTime.Now:O}");
    Thread.Sleep(TimeSpan.FromSeconds(intervaloSegundos));
}

