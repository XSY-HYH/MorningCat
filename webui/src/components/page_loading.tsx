import { Spinner } from '@heroui/spinner';
import clsx from 'clsx';

export interface PageLoadingProps {
  loading?: boolean;
}
const PageLoading: React.FC<PageLoadingProps> = ({ loading = true }) => {
  return (
    <div
      className={clsx(
        'w-full h-full min-h-screen flex justify-center items-center',
        {
          hidden: !loading,
        }
      )}
    >
      <Spinner size='lg' />
    </div>
  );
};

export default PageLoading;
