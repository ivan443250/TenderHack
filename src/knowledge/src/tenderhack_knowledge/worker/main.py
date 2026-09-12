import asyncio


async def run_worker() -> None:
    await asyncio.Event().wait()


def run() -> None:
    asyncio.run(run_worker())


if __name__ == "__main__":
    run()
