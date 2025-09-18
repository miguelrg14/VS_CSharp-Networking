// Miguel Rodríguez Gallego

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Servidor
{
    class Nodo
    {
        // Variables
        public Nodo siguiente;
        public Socket Socket;
        public string nombre;

        // Constructor
        public Nodo(Socket SocketAGuardar)
        {
            Socket = SocketAGuardar;
            siguiente = null;
        }

        // Constructor de sobrecarga
        public Nodo(Socket SocketAGuardar, string nick)
        {
            nombre = nick;
            Socket = SocketAGuardar;
            siguiente = null;
        }
    }

    class Lista
    {
        Nodo cabeza;

        // Añadimos un nuevo cliente a la lista
        public void AñadirCliente(Socket NuevoCliente, string nick)
        {
            Nodo nuevoCliente;
            Nodo anteriorACabeza;

            // Transferencia de los datos: Cliente ==> Nodo
            nuevoCliente = new Nodo(NuevoCliente, nick);

            // Si la lista está vacia se pasa el nodo a la cabeza
            if (ListaVacia())
            {
                nuevoCliente.siguiente = cabeza;
                cabeza = nuevoCliente;
            }
            else
            {
                //Sino lo introducimos detras de la cabeza
                anteriorACabeza = cabeza;

                while (anteriorACabeza.siguiente != null)
                {
                    anteriorACabeza = anteriorACabeza.siguiente;
                }

                nuevoCliente.siguiente = anteriorACabeza.siguiente;
                anteriorACabeza.siguiente = nuevoCliente;
            }
        }

        public void EnviarMensaje(Socket Transmisor, string mensaje, string nuevoCliente)
        {
            // Creo el nodo "indice", el cual estará unido a los datos y podrá servir para ver si la lista está vacía o no
            Nodo indice;

            // Si la lista está vacía no hay clientes a los que enviar el mensaje
            if (ListaVacia())
            {
                Console.WriteLine("No hay gente para chatear");
            }

            // Si la lista está llena se envía el mensaje
            else
            {
                indice = cabeza;

                while (indice != null)
                {
                    if (indice.Socket == Transmisor)
                    {
                        // Hago esto para que el transmisor del mensaje no reciba su propio msg
                    }
                    else
                    {
                        // Envío el mensaje al resto de clientes del chat
                        byte[] bytesAEnviar = Encoding.Default.GetBytes(nuevoCliente + ": " + mensaje);
                        indice.Socket.Send(bytesAEnviar, 0, bytesAEnviar.Length, SocketFlags.None);
                    }

                    indice = indice.siguiente;
                }
            }
        }

        // Borra un dato de la lista
        public void BorrarDato(Socket datoABorrar)
        {
            Nodo indice;

            // Si la lista está vacía no borra nada
            if (ListaVacia())
            {
                Console.WriteLine("Lista vacía, introduce un dato...");
            }

            // Si la lista no está vacía compruebo la cabeza para ver el dato que necesito eliminar
            else
            {                
                indice = cabeza;

                // Si la cabeza es la que quiero borrar, desenlazo el nodo para que pierda los datos
                if (cabeza.Socket == datoABorrar)
                {                    
                    cabeza = indice.siguiente;

                    if (indice.siguiente == null)
                    {
                        cabeza = null;
                    }
                    else
                    {
                        indice.siguiente = indice.siguiente.siguiente;
                    }
                }
                else
                {
                    // Si no es la cabeza se hace lo mismo pero sin tocar la cabeza
                    while (indice.siguiente.Socket != datoABorrar)
                    {
                        indice = indice.siguiente;
                    }

                    indice.siguiente = indice.siguiente.siguiente;
                }
            }
        }

        // Comprueba si la lista está vacía o no
        public bool ListaVacia()
        {
            if (cabeza == null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        
        class Servidor
        {
            private const int listenPort = 12000;

            public static void Main()
            {
                bool cerrarServidor = false;
                int contadorClientes = 1;

                // Creo una lista para almacenar los datos de los clientes
                Lista lista = new Lista();

                //Inicializamos el socket. Será un socket TCP
                Socket mySocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                //Tenemos que decirle dónde se va a conectar este socket, 
                //en este caso al ser el servidor le metemos el IPAddress.Any
                //que quiere decir que el servidor escuchará en todas las interfaces
                //, y el puerto 12000
                IPEndPoint direccion = new IPEndPoint(IPAddress.Any, listenPort);

                try
                {
                    //Ahora que tenemos el socket y la dirección, tenemos que enlazarlos, para
                    //eso usamos bind
                    mySocket.Bind(direccion);

                    do
                    {

                        //Lo ponemos en "modo escucha", y decimos que acepte solo 3 cliente de cada vez
                        mySocket.Listen(5);

                        Console.WriteLine("Servidor escuchando a un cliente...");

                        //Aquí nos ponemos a "escuchar". Esto quiere decir que la ejecución de este programa se 
                        //bloquea hasta que alguien se conecte al socket
                        //Este método como ves, devuelve un socket que he llamado clientsocket. Este clientsocket
                        //es el socket por el que escribiremos y leeremos del cliente. El socket mysocket solo lo usaremos
                        //para recibir las peticiones de conexión
                        Socket clientSocket = mySocket.Accept(); //<-- La ejecución se va a parar aquí hasta que alguien se conecte


                        // Pido el nombre al cliente
                        byte[] bytesAEnviar = Encoding.Default.GetBytes("Introduce un nombre por favor");
                        clientSocket.Send(bytesAEnviar, 0, bytesAEnviar.Length, SocketFlags.None);

                        // Recibo el nombre
                        byte[] bytesARecibir = new byte[512];
                        // Leo el socket y lo guardo 
                        int bytesRecibidos = clientSocket.Receive(bytesARecibir, 0, bytesARecibir.Length, SocketFlags.None);
                        // Borro el array que sobra
                        Array.Resize(ref bytesARecibir, bytesRecibidos);
                        // Paso el arrays de bytes a string para poder leerlo y verlo por pantalla 
                        string nick = Encoding.Default.GetString(bytesARecibir);


                        // Creo un nuevo hilo para cada cliente 
                        Thread hiloServidor = new Thread(() => HiloCliente(clientSocket, lista, nick));

                        hiloServidor.Start();

                        // Meto a los clientes dentro de la lista
                        lista.AñadirCliente(clientSocket, nick);

                        Console.WriteLine("Cliente: " + nick + " ==> Conectado!");

                        contadorClientes++;
                    }
                    while (cerrarServidor == false);

                    //Cerramos el socket al final de todo, pero esto no pasa porq nunca se llega a cerrar, escucha constantemente
                    mySocket.Close();

                }
                catch (Exception error)
                {
                    Console.WriteLine("Error: {0}", error.ToString());
                }

                Console.WriteLine("Presione cualquier tecla para terminar");
                Console.ReadLine();

            }

            public static void HiloCliente(Socket clientSocket, Lista listaClientes, string nick)
            {
                string mensajeRecibido;

                //Bucle para recibir y mandar mensajes
                do
                {
                    //Recibimos mensajes del servidor
                    //Creamos el array de bytes para almacenar el mensaje recibido
                    byte[] bytesARecibir = new byte[512];
                    //Recibimos el mensaje del cliente y lo pasamos a un array de bits
                    int bytesRecibidos = clientSocket.Receive(bytesARecibir, 0, bytesARecibir.Length, SocketFlags.None);
                    //Corta el array sobrante
                    Array.Resize(ref bytesARecibir, bytesRecibidos);
                    //Pasamos el arrays de bytes a string para poder leerlo y lo mostramos por pantalla :D
                    mensajeRecibido = Encoding.Default.GetString(bytesARecibir);

                    //Debug para ver que mensajes están rulando por el servidor
                    Console.WriteLine(nick + ": " + mensajeRecibido);


                    listaClientes.EnviarMensaje(clientSocket, mensajeRecibido, nick);
                    

                    Thread.Sleep(500);
                }
                while (mensajeRecibido != "salir");
                                
                Console.WriteLine("El cliente ha cerrado la conexión");
                listaClientes.BorrarDato(clientSocket);
                clientSocket.Close();
            }
        }
    }
}
