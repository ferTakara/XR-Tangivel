#if !defined(__ANDROID__)
#include <opencv2/opencv.hpp>
#include <opencv2/objdetect.hpp>
#include <opencv2/objdetect/aruco_dictionary.hpp>
#include <opencv2/objdetect/aruco_detector.hpp>
#include <vector>

#if defined(_WIN32)
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API __attribute__((visibility("default")))
#endif

extern "C" {
    // Nova função: detecta múltiplos marcadores simultaneamente
    EXPORT_API int DetectarMultiplosArucos(
        unsigned char* pixels, 
        int width,
        int height,
        int channels,
        int maxMarcadores,
        int* outIds,
        float* outCorners 
    ) {
        cv::Mat gray;
        if (channels == 4) {
            // No Editor (XR Simulation), imagem com 4 canais (RGBA)
            cv::Mat rgba(height, width, CV_8UC4, pixels);
            cv::cvtColor(rgba, gray, cv::COLOR_RGBA2GRAY);
        } else {
            // No celular, plano Y é Grayscale (1 canal)
            gray = cv::Mat(height, width, CV_8UC1, pixels);
        }

        std::vector<int> markerIds;
        std::vector<std::vector<cv::Point2f>> markerCorners;

        cv::aruco::Dictionary dictionary = cv::aruco::getPredefinedDictionary(cv::aruco::DICT_6X6_1000);
        cv::aruco::DetectorParameters detectorParams = cv::aruco::DetectorParameters();
        cv::aruco::ArucoDetector detector(dictionary, detectorParams);

        detector.detectMarkers(gray, markerCorners, markerIds);

        int totalAchados = (int)markerIds.size();
        int qtdCopiar = (totalAchados > maxMarcadores) ? maxMarcadores : totalAchados;

        for (int i = 0; i < qtdCopiar; i++) {
            outIds[i] = markerIds[i];

            // 4 cantos para cada marcador (8 floats por marcador)
            for (int j = 0; j < 4; j++) {
                outCorners[i * 8 + j * 2] = markerCorners[i][j].x;
                outCorners[i * 8 + j * 2 + 1] = markerCorners[i][j].y;
            }
        }

        return qtdCopiar;
    }

    // Função legada para compatibilidade com versões anteriores
    EXPORT_API int DetectarAruco(
        unsigned char* pixels, 
        int width,
        int height,
        int channels,
        float* outCorners 
    ) {
        int id = -1;
        int n = DetectarMultiplosArucos(pixels, width, height, channels, 1, &id, outCorners);
        return (n > 0) ? id : -1;
    }
}
#endif
