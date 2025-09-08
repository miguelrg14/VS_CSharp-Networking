// Miguel Rodríguez Gallego

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Net;

namespace Cliente
{
    class Cliente
    {
        static void Main(string[] args)
        {
            string textoAEnviar;

            //Los tres primeros pasos son iguales que para el servidor, creamos un socket
            //elegimos una dirección y enlazamos el socket. La dirección es la misma.
            Socket mySocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint direccion = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12000);

            try
            {
                // Conexión: Cliente ==> Servidor
                Console.WriteLine("Intentando conexión...");
                mySocket.Connect(direccion);
                Console.WriteLine("Conectado. Introduce algo para mandar al servidor: ");
                Console.WriteLine("Escribe salir para abandonar el chat");

                // Recibo el texto en bytes y pido el nombre
                byte[] bytesARecibir = new byte[512];
                int bytesRecibidos = 0;
                bytesRecibidos = mySocket.Receive(bytesARecibir, 0, bytesARecibir.Length, SocketFlags.None);
                Array.Resize(ref bytesARecibir, bytesRecibidos);
                string nick = Encoding.Default.GetString(bytesARecibir);
                Console.WriteLine(nick);

                // El usuario contesta con su nombre
                Console.Write("Nombre: ");
                Console.Write("> ");
                textoAEnviar = Console.ReadLine();
                byte[] bytesAEnviar = Encoding.Default.GetBytes(textoAEnviar);
                mySocket.Send(bytesAEnviar, 0, bytesAEnviar.Length, SocketFlags.None);

                // Creo un hilo para que el cliente pueda recibir mensajes sin tiempo de espera
                Thread hiloClientes = new Thread(() => EsperaMensajes(mySocket));

                hiloClientes.Start();

                // Bucle para poder salir del servidor tras escribir "salir"
                do
                {
                    // El ordenador lee el mensaje que quiero enviar al servidor
                    Console.Write("> ");
                    textoAEnviar = Console.ReadLine();
                    bytesAEnviar = Encoding.Default.GetBytes(textoAEnviar);

                    // Se envían los datos
                    mySocket.Send(bytesAEnviar, 0, bytesAEnviar.Length, SocketFlags.None);

                }
                while (textoAEnviar != "salir");

                // Se cierra el socket
                Console.WriteLine("Has salido de la conversación...");

                mySocket.Close();
            }
            catch (Exception error)
            {
                Console.WriteLine("Error: {0}", error.ToString());
            }
            Console.WriteLine("Presione cualquier tecla para terminar");
            Console.ReadLine();
        }

        private static void EsperaMensajes(Socket mySocket)
        {
            string mensajeRecibido;

            do
            {
                // Creo un array para almacenar los datos
                byte[] bytesARecibir = new byte[512];
                int bytesRecibidos;

                // Try - Catch por si hay algun dato a recibir
                try
                {
                    // Recibo el mensaje del servidor
                    bytesRecibidos = mySocket.Receive(bytesARecibir, 0, bytesARecibir.Length, SocketFlags.None);

                    // Revalorizo el tamaño del array
                    Array.Resize(ref bytesARecibir, bytesRecibidos);

                    // Paso el arrays de bytes a string para poder leerlo y verlo por pantalla
                    mensajeRecibido = Encoding.Default.GetString(bytesARecibir);
                    Console.WriteLine(mensajeRecibido);
                    Console.Write("> ");
                }
                // Cuando rompa lo cierro
                catch
                {
                    mensajeRecibido = "cerrado";
                }
            }
            while (mensajeRecibido != "cerrado");
        }
    }
}
