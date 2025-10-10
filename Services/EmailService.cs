using AlarmaDisparadorCore.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;

namespace AlarmaDisparadorCore.Services
{
    public class EmailService
    {
        private readonly bool _enabled;
        private readonly string _host;
        private readonly int _port;
        private readonly bool _enableSsl;
        private readonly string _userName;
        private readonly string _password;
        private readonly string _from;
        private readonly string _subjectPrefix;

        public EmailService(IConfiguration configuration)
        {
            if (configuration == null)
            {
                _enabled = false;
                return;
            }

            var section = configuration.GetSection("Email");
            if (!section.Exists())
            {
                _enabled = false;
                return;
            }

            _enabled = section.GetValue("Enabled", true);
            _host = section.GetValue<string>("SmtpHost");
            _port = section.GetValue("SmtpPort", 25);
            _enableSsl = section.GetValue("EnableSsl", false);
            _userName = section.GetValue<string>("UserName");
            _password = section.GetValue<string>("Password");
            _from = section.GetValue<string>("From");
            _subjectPrefix = section.GetValue<string>("SubjectPrefix", "");

            if (string.IsNullOrWhiteSpace(_from) && !string.IsNullOrWhiteSpace(_userName))
            {
                _from = _userName;
            }
        }

        public void EnviarCorreo(ReglaAlarma regla)
        {
            try
            {
                if (!_enabled)
                {
                    Logger.Log("Envío de correo deshabilitado");
                    return;
                }

                if (regla == null)
                    return;

                if (!regla.EnviarCorreo)
                    return;

                var destinatarios = ObtenerDestinatarios(regla.EmailDestino);
                if (destinatarios.Count == 0)
                {
                    Logger.Log("Regla sin destinatarios de correo configurados");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_host))
                {
                    Logger.Log("Configuración SMTP incompleta: host");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_from))
                {
                    Logger.Log("Configuración SMTP incompleta: remitente");
                    return;
                }

                using var message = new MailMessage()
                {
                    From = new MailAddress(_from),
                    Subject = ConstruirAsunto(regla),
                    Body = ConstruirCuerpo(regla),
                    IsBodyHtml = false
                };

                foreach (var destinatario in destinatarios)
                {
                    message.To.Add(destinatario);
                }

                using var client = new SmtpClient(_host, _port)
                {
                    EnableSsl = _enableSsl
                };

                if (!string.IsNullOrWhiteSpace(_userName) && !string.IsNullOrWhiteSpace(_password))
                {
                    client.Credentials = new NetworkCredential(_userName, _password);
                }
                else
                {
                    client.UseDefaultCredentials = true;
                }

                client.Send(message);

                Logger.Log($"Correo enviado para la regla '{regla.Nombre}' a {string.Join(", ", destinatarios)}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, nameof(EnviarCorreo));
            }
        }

        private static List<string> ObtenerDestinatarios(string? emailDestino)
        {
            if (string.IsNullOrWhiteSpace(emailDestino))
                return new List<string>();

            return emailDestino
                .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string ConstruirAsunto(ReglaAlarma regla)
        {
            var asunto = $"Alarma disparada: {regla.Nombre}";
            if (!string.IsNullOrWhiteSpace(_subjectPrefix))
            {
                return $"{_subjectPrefix} {asunto}".Trim();
            }

            return asunto;
        }

        private static string ConstruirCuerpo(ReglaAlarma regla)
        {
            return $"Regla: {regla.Nombre}{Environment.NewLine}Mensaje: {regla.Mensaje}{Environment.NewLine}Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }
    }
}
