import { serverRequest } from '@/utils/request';

export default class ProcessManager {
  public static async restartProcess () {
    const data = await serverRequest.post<ServerResponse<{ message: string; }>>(
      '/Process/Restart'
    );

    return data.data.data;
  }

  public static async shutdownProcess () {
    const data = await serverRequest.post<ServerResponse<{ message: string; }>>(
      '/Process/Shutdown'
    );

    return data.data.data;
  }
}
