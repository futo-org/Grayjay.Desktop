#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <signal.h>
#include <setjmp.h>
#include <sys/mman.h>
#include <unistd.h>
#include "blink/winmap.h"
ssize_t VfsPread(int fd,void *buf,size_t n,off_t off) {return pread(fd,buf,n,off);}
static sigjmp_buf fault;
static void segv(int sig) {siglongjmp(fault,1);}
int main(void) {
 signal(SIGSEGV,segv);
 char *a=WindowsMmap(0,4096,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS,-1,0);assert(a!=MAP_FAILED);
 char *b=WindowsMmap(a+4096,4096,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS,-1,0);assert(b==a+4096);
 a[0]=42;b[0]=43;
 assert(!WindowsMprotect(a,4096,PROT_READ));
 if(!sigsetjmp(fault,1)){*(volatile char*)a=1;assert(!"readonly write did not fault");}
 assert(a[0]==42);b[0]=44;assert(b[0]==44);
 assert(WindowsMmap(b,4096,3,MAP_PRIVATE|MAP_ANONYMOUS,-1,0)==MAP_FAILED);assert(errno==EEXIST);assert(b[0]==44);
 assert(!WindowsMunmap(a,4096));assert(b[0]==44);
 assert(WindowsMmap(a,4096,3,MAP_FIXED|MAP_PRIVATE|MAP_ANONYMOUS,-1,0)==a);assert(a[0]==0);assert(b[0]==44);
 assert(!WindowsMprotect(a,4096,PROT_NONE));
 if(!sigsetjmp(fault,1)){volatile char c=*(volatile char*)a;(void)c;assert(!"inaccessible read did not fault");}
 assert(WindowsMprotect(a+8192,4096,PROT_READ)==-1);
 int local=99;assert(WindowsMmap((void*)((uintptr_t)&local&~(uintptr_t)4095),4096,3,MAP_FIXED|MAP_PRIVATE|MAP_ANONYMOUS,-1,0)==MAP_FAILED);assert(local==99);
 char name[]="/tmp/winmap-test.XXXXXX";int fd=mkstemp(name);assert(fd>=0);unlink(name);assert(!ftruncate(fd,8192));assert(pwrite(fd,"abcd",4,4096)==4);
 char *file=WindowsMmap(0,4096,PROT_READ,MAP_PRIVATE,fd,4096);assert(file!=MAP_FAILED);assert(file[0]=='a'&&file[3]=='d'&&file[4]==0);
 assert(WindowsMmap(0,4096,PROT_READ,MAP_SHARED,fd,0)==MAP_FAILED);assert(errno==ENOTSUP);close(fd);assert(file[1]=='b');
 char *lazy=WindowsMmap(0,64*1024*1024,PROT_NONE,MAP_PRIVATE|MAP_ANONYMOUS,-1,0);assert(lazy!=MAP_FAILED);
 MEMORY_BASIC_INFORMATION info;assert(VirtualQuery(lazy,&info,sizeof(info)));assert(info.State==MEM_RESERVE);
 assert(!WindowsMprotect(lazy+4096,4096,PROT_READ|PROT_WRITE));assert(VirtualQuery(lazy+4096,&info,sizeof(info)));assert(info.State==MEM_COMMIT);lazy[4096]=42;
 assert(VirtualQuery(lazy+8192,&info,sizeof(info)));assert(info.State==MEM_RESERVE);assert(!WindowsMprotect(lazy+4096,4096,PROT_NONE));assert(!WindowsMprotect(lazy+4096,4096,PROT_READ));assert(lazy[4096]==42);
 assert(!WindowsMunmap(lazy,64*1024*1024));
 assert(!WindowsOwnsMap(lazy));
 assert(VirtualQuery(lazy,&info,sizeof(info)));assert(info.State==MEM_FREE);
 assert(WindowsMmap(0,4096,PROT_READ|PROT_WRITE,MAP_SHARED|MAP_ANONYMOUS,-1,0)==MAP_FAILED);assert(errno==ENOTSUP);
 puts("PASS 4 KiB protections, adjacent pages, zeroed remapping, collision safety, file offsets and shared-file rejection");
}
