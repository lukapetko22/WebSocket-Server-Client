import websocket
import time
from time import sleep
from threading import Thread
import sys
import random

def ws_client(id, interval, n):
    sleep(interval)
    ws = websocket.create_connection("ws://127.0.0.1/")
    print("Client", id, "connected!")
    for i in range(n):
        #Implementing the whole gps NMEA simulator would be a bit of a pain
        lat = 44.8159745 + random.randint(0, 5)
        lon = 20.4601541 + random.randint(0, 5)
        data = ",".join([str(id), str(int(time.time())), str(lat), str(lon)])
        print("Sending data:", data)
        ws.send(data)
        
        print("Client", id, "received:", ws.recv())
        sleep(interval)
    ws.close()
    print("Client", id, "disconnected!")


n = int(sys.argv[1])
threads=[]
for i in range(n):
    interval = random.randint(1, 3)
    reps = random.randint(1, 5)
    print("Creating client with ID:", i+1, ", interval:", interval, ", repetitions:", reps)
    t = Thread(target=ws_client, args=(i+1, interval, reps, ))
    t.daemon = True
    t.start()
    threads.append(t)

while(True):
    finished = True
    for t in threads:
        if t.is_alive():
            finished = False
    if(finished == True):
        print()
        print("Simulation done, all clients disconnected.")
        quit()

    sleep(1)