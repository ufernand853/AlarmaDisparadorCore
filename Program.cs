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
    try
    {
        Logger.Log("Inicio de evaluación");
        evaluador.EvaluarReglas();
        Logger.Log("Fin de evaluación");
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "MainLoop");
    }

    Thread.Sleep(TimeSpan.FromSeconds(intervaloSegundos));
}

