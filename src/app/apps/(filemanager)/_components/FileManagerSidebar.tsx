import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Progress } from "reactstrap";
import {
  IconAdjustmentsAlt,
  IconFolderOpen,
  IconHelp,
  IconRotateClockwise2,
  IconSend,
  IconStar,
  IconTrash,
} from "@tabler/icons-react";
import { Files, Image as FileTypeIcon } from "phosphor-react";
import { FileArchive, Video } from "@phosphor-icons/react";
import Loading from "@/app/loading";

type FileManagerSidebarProps = {
  activeTab: string;
  setActiveTab: (tab: string) => void;
};

const fileCategories = [
  {
    label: "Images",
    icon: <FileTypeIcon size={18} weight="duotone" />,
    colorClass: "text-light-primary",
    files: "1.195 Files",
    size: "37.2GB",
  },
  {
    label: "Videos",
    icon: <Video size={18} weight="duotone" />,
    colorClass: "text-light-success",
    files: "53 Files",
    size: "19.1 GB",
  },
  {
    label: "Documents",
    icon: <FileArchive size={18} weight="duotone" />,
    colorClass: "text-light-danger",
    files: "486 Files",
    size: "23.5 MB",
  },
  {
    label: "Others",
    icon: <Files size={18} weight="duotone" />,
    colorClass: "text-light-warning",
    files: "32 Files",
    size: "13 MB",
  },
];

const FileManagerSidebar: React.FC<FileManagerSidebarProps> = ({
  activeTab,
  setActiveTab,
}) => {
  const handleTabClick = (tab: string) => {
    setActiveTab(tab);
  };
  const [ApexCharts, setApexCharts] = useState<any>(null);

  useEffect(() => {
    const loadModules = async () => {
      const [chartModule] = await Promise.all([import("react-apexcharts")]);

      setApexCharts(() => chartModule.default || chartModule);
    };

    if (typeof window !== "undefined") {
      loadModules();
    }
  }, []);

  return (
    <>
      <Card>
        <CardHeader>
          <h5>My Drive</h5>
        </CardHeader>

        <CardBody>
          <div className="horizontal-tab-wrapper">
            <ul className="filemenu-list mt-3 tabs">
              <li
                className={`tab-link align-items-center ${activeTab === "1" ? "active" : ""}`}
                onClick={() => handleTabClick("1")}
                data-tab="1"
              >
                <IconFolderOpen size={30} className=" pe-2" />
                <span className="flex-grow-1">My Cloud</span>
                10+
              </li>

              <li
                className={`tab-link align-items-center ${activeTab === "2" ? "active" : ""}`}
                onClick={() => handleTabClick("2")}
                data-tab="2"
              >
                <IconStar size={28} className=" pe-2" />
                <span className="flex-grow-1">Starred</span>
              </li>

              <li
                className={`tab-link align-items-center ${activeTab === "3" ? "active" : ""}`}
                onClick={() => handleTabClick("3")}
                data-tab="3"
              >
                <IconTrash size={28} className=" pe-2" />
                <span className="flex-grow-1">Recycle Bin</span>
                2+
              </li>

              <li
                className={`tab-link align-items-center ${activeTab === "4" ? "active" : ""}`}
                onClick={() => handleTabClick("4")}
                data-tab="4"
              >
                <IconRotateClockwise2 size={28} className=" pe-2" />
                <span className="flex-grow-1">Recent</span>
              </li>

              <div className="app-divider-v dashed m-0 p-2"></div>

              <li
                className="tab-link align-items-center"
                onClick={() => handleTabClick("5")}
              >
                <IconSend size={28} className="pe-2" />
                <span className="flex-grow-1">Shared File</span>
              </li>

              <li
                className="tab-link align-items-center"
                onClick={() => handleTabClick("6")}
              >
                <IconHelp size={28} className=" pe-2" />
                <span className="flex-grow-1">Help</span>
              </li>

              <li
                className="tab-link align-items-center"
                onClick={() => handleTabClick("7")}
              >
                <IconAdjustmentsAlt size={28} className=" pe-2" />
                <span className="flex-grow-1">Settings</span>
              </li>
            </ul>
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader>
          <h5>Overview</h5>
        </CardHeader>

        <CardBody>
          <div className="mb-3">
            {ApexCharts ? (
              <ApexCharts
                series={[70]}
                type={"radialBar"}
                height={350}
                options={{
                  chart: {
                    offsetY: -20,
                    sparkline: {
                      enabled: true,
                    },
                  },
                  colors: ["rgba(var(--primary),1)"],
                  plotOptions: {
                    radialBar: {
                      startAngle: -90,
                      endAngle: 90,
                      track: {
                        background: "#e7e7e7",
                        strokeWidth: "97%",
                        margin: 5, // margin is in pixels
                        dropShadow: {
                          enabled: true,
                          top: 2,
                          left: 0,
                          color: "#999",
                          opacity: 1,
                          blur: 2,
                        },
                      },
                      dataLabels: {
                        name: {
                          show: false,
                        },
                        value: {
                          offsetY: -4,
                          fontSize: "22px",
                        },
                      },
                    },
                  },
                  grid: {
                    padding: {
                      top: -20,
                    },
                  },
                  fill: {
                    type: "",
                    gradient: {
                      shade: "",
                      shadeIntensity: 0.4,
                      inverseColors: false,
                      opacityFrom: 1,
                      opacityTo: 1,
                      stops: [0, 60, 73, 108],
                    },
                  },
                  labels: ["Average Results"],
                }}
              />
            ) : (
              <Loading />
            )}
          </div>
          {fileCategories.map((category, index) => (
            <div className="file-manager-sidebar mb-4" key={index}>
              <div className="d-flex align-items-center position-relative">
                <span
                  className={`${category.colorClass} h-40 w-40 d-flex-center b-r-10 position-absolute`}
                >
                  {category.icon}
                </span>
                <div className="flex-grow-1 ms-5">
                  <h6 className="mb-0">{category.label}</h6>
                  <p className="text-secondary mb-0">{category.files}</p>
                </div>
                <p className="text-secondary f-w-500 mb-0">{category.size}</p>
              </div>
            </div>
          ))}
        </CardBody>
      </Card>
      <Card>
        <CardHeader>
          <h5>File Upload</h5>
        </CardHeader>
        <CardBody>
          <div className="mb-4">
            <h6 className="mb-1 text-dark">Uploading 59 photos</h6>
            <div>
              <div className="d-flex justify-content-between">
                <p className="text-secondary">Photoes 01</p>
                <span className="text-primary">65%</span>
              </div>
              <Progress className="h-5" value={65} max={100} color="primary" />
            </div>
          </div>

          <div className="mb-4">
            <h6 className="mb-1 text-dark">Uploading 7 videos</h6>
            <div>
              <div className="d-flex justify-content-between">
                <p className="text-secondary">Museum</p>
                <span className="text-primary">25%</span>
              </div>
              <Progress className="h-5" value={25} max={100} color="primary" />
            </div>
          </div>

          <div className="mb-4">
            <h6 className="mb-1 text-dark">Uploading 12 Documents</h6>
            <div>
              <div className="d-flex justify-content-between">
                <p className="text-secondary">My Work</p>
                <span className="text-primary">90%</span>
              </div>
              <Progress className="h-5" value={90} max={100} color="primary" />
            </div>
          </div>
        </CardBody>
      </Card>
    </>
  );
};

export default FileManagerSidebar;
