#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdint.h>
#include <sys/mman.h>
#include <unistd.h>
#include "blink/winmap.h"
ssize_t VfsPread(int fd,void *buf,size_t n,off_t off) {return pread(fd,buf,n,off);}
int main(void) {
  // Run under a 32 MiB job ceiling. A real commit failure must release every
  // partial reservation and preserve an unrelated neighboring mapping.
  char *base=(char *)(uintptr_t)0x310000000000;
  char *neighbor=WindowsMmap(base,4096,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS|MAP_FIXED,-1,0);
  assert(neighbor==base);neighbor[0]=42;
  assert(WindowsMmap(base+65536,512UL*1024*1024,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS|MAP_FIXED,-1,0)==MAP_FAILED);
  assert(errno==ENOMEM);assert(neighbor[0]==42);
  assert(!WindowsOwnsMap(base+65536));
  MEMORY_BASIC_INFORMATION info;
  assert(VirtualQuery(base+65536,&info,sizeof(info)));assert(info.State==MEM_FREE);
  assert(!WindowsMunmap(neighbor,4096));assert(!WindowsOwnsMap(neighbor));
  puts("PASS failed mapping rolls back partial commits and reservations without altering its neighbor");
}
