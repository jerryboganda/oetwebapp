"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem, hexToRGB } from "@/_helper";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const RadialbarChartPage = () => {
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
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Radialbar"
          title="Chart"
          path={["Apexcharts", "Radialbar"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col lg="6" xxl="4">
            <Card>
              <CardHeader>
                <h5>Basic RadialBar Chart</h5>
              </CardHeader>
              <CardBody className="bg-primary-200">
                {ApexCharts ? (
                  <ApexCharts
                    series={[70]}
                    type={"radialBar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "radialBar",
                      },
                      plotOptions: {
                        radialBar: {
                          hollow: {
                            size: "70%",
                          },
                        },
                      },
                      labels: ["Cricket"],
                      colors: [getLocalStorageItem("color-primary", "#8973ea")],
                      responsive: [
                        {
                          breakpoint: 567,
                          options: {
                            chart: {
                              height: 250,
                            },
                          },
                        },
                      ],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6" xxl="4">
            <Card>
              <CardHeader>
                <h5> Multiple RadialBars</h5>
              </CardHeader>
              <CardBody className="bg-secondary-200">
                {ApexCharts ? (
                  <ApexCharts
                    series={[44, 55, 67, 83]}
                    type={"radialBar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "radialBar",
                      },
                      plotOptions: {
                        radialBar: {
                          dataLabels: {
                            name: {
                              fontSize: "22px",
                            },
                            value: {
                              fontSize: "16px",
                            },

                            total: {
                              show: true,
                              label: "Total",
                              formatter: function () {
                                return "249";
                              },
                            },
                          },
                        },
                      },
                      labels: ["Apples", "Oranges", "Bananas", "Berries"],
                      responsive: [
                        {
                          breakpoint: 567,
                          options: {
                            chart: {
                              height: 250,
                            },
                          },
                        },
                      ],
                      colors: [
                        hexToRGB(
                          getLocalStorageItem("color-primary", "#147534"),
                          1
                        ),
                      ],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6" xxl="4">
            <Card>
              <CardHeader>
                <h5> Circle Chart – Custom Angle</h5>
              </CardHeader>
              <CardBody className=" bg-success-200">
                {ApexCharts ? (
                  <ApexCharts
                    series={[76, 67, 61, 90]}
                    type={"radialBar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 390,
                        type: "radialBar",
                      },
                      plotOptions: {
                        radialBar: {
                          offsetY: 0,
                          startAngle: 0,
                          endAngle: 270,
                          hollow: {
                            margin: 5,
                            size: "30%",
                            background: "transparent",
                            image: undefined,
                          },
                          dataLabels: {
                            name: {
                              show: false,
                            },
                            value: {
                              show: false,
                            },
                          },
                        },
                      },
                      colors: [
                        getLocalStorageItem("color-primary", "#8973ea"),
                        "#147534",
                        getLocalStorageItem("color-secondary", "#626263"),
                        "#e90bc4",
                      ],
                      labels: ["Vimeo", "Messenger", "Facebook", "LinkedIn"],
                      legend: {
                        show: true,
                        floating: true,
                        fontSize: "16px",
                        position: "left",
                        offsetX: 20,
                        offsetY: 20,
                        labels: {
                          useSeriesColors: true,
                        },
                        markers: {
                          size: 0,
                        },
                        formatter: function (seriesName: string, opts: any) {
                          return (
                            seriesName +
                            ":  " +
                            opts.w.globals.series[opts.seriesIndex]
                          );
                        },
                        itemMargin: {
                          vertical: 3,
                        },
                      },
                      responsive: [
                        {
                          breakpoint: 1550,
                          options: {
                            legend: {
                              offsetX: -5,
                              offsetY: 15,
                            },
                          },
                        },
                        {
                          breakpoint: 567,
                          options: {
                            chart: {
                              height: 250,
                            },
                          },
                        },
                        {
                          breakpoint: 480,
                          options: {
                            legend: {
                              fontSize: "15px",
                              offsetX: -30,
                              offsetY: -10,
                            },
                          },
                        },
                      ],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6" xxl="4">
            <Card className="equal-card">
              <CardHeader>
                <h5> Stroked Circular Gauge</h5>
              </CardHeader>
              <CardBody className="bg-warning-200">
                {ApexCharts ? (
                  <ApexCharts
                    series={[67]}
                    type={"radialBar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        offsetY: -10,
                      },
                      plotOptions: {
                        radialBar: {
                          startAngle: -135,
                          endAngle: 135,
                          dataLabels: {
                            name: {
                              fontSize: "16px",
                              color: undefined,
                              offsetY: 120,
                            },
                            value: {
                              offsetY: 76,
                              fontSize: "22px",
                              color: undefined,
                              formatter: function (val: number) {
                                return val + "%";
                              },
                            },
                          },
                        },
                      },
                      fill: {
                        colors: ["#48443D"],
                      },
                      stroke: {
                        dashArray: 4,
                      },
                      labels: ["Median Ratio"],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6" xxl="4">
            <Card className="equal-card">
              <CardHeader>
                <h5> Semi Circular Gauge</h5>
              </CardHeader>
              <CardBody className="bg-info-200">
                {ApexCharts ? (
                  <ApexCharts
                    series={[76]}
                    type={"radialBar"}
                    height={410}
                    options={{
                      chart: {
                        height: 410,
                        type: "radialBar",
                        offsetY: -20,
                        sparkline: {
                          enabled: true,
                        },
                      },
                      plotOptions: {
                        radialBar: {
                          startAngle: -90,
                          endAngle: 90,
                          track: {
                            background: "#e7e7e7",
                            strokeWidth: "97%",
                            margin: 5,
                          },
                          dataLabels: {
                            name: {
                              show: false,
                            },
                            value: {
                              offsetY: -2,
                              fontSize: "22px",
                            },
                          },
                        },
                      },
                      grid: {
                        padding: {
                          top: -10,
                        },
                      },
                      fill: {
                        colors: [
                          getLocalStorageItem("color-primary", "#E90BC4"),
                        ],
                      },
                      labels: ["Average Results"],
                      responsive: [
                        {
                          breakpoint: 1366,
                          options: {
                            chart: {
                              height: 500,
                            },
                          },
                        },
                        {
                          breakpoint: 567,
                          options: {
                            chart: {
                              height: 250,
                            },
                          },
                        },
                      ],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6" xxl="4">
            <Card className="equal-card">
              <CardHeader>
                <h5> Circle Chart with Image</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[67]}
                    type={"radialBar"}
                    height={350}
                    options={{
                      plotOptions: {
                        radialBar: {
                          hollow: {
                            margin: 15,
                            size: "70%",
                            image: "/images/icons/clock.png",
                            imageWidth: 64,
                            imageHeight: 64,
                            imageClipped: false,
                          },
                          dataLabels: {
                            name: {
                              show: false,
                            },
                            value: {
                              show: true,
                              color: "#333",
                              offsetY: 70,
                              fontSize: "22px",
                            },
                          },
                        },
                      },
                      fill: {
                        type: "image",
                        image: {
                          src: ["/images/slick/11.jpg"],
                        },
                      },
                      stroke: {
                        lineCap: "round",
                      },
                      labels: ["Volatility"],
                      responsive: [
                        {
                          breakpoint: 567,
                          options: {
                            chart: {
                              height: 250,
                            },
                          },
                        },
                      ],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Custom size and custom thickness</h5>
                <p>You can use customize size by adding</p>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-wrap custom-radial justify-content-center">
                  <div className="d-flex align-items-center flex-column">
                    <div className="mt-auto">
                      {ApexCharts ? (
                        <ApexCharts
                          series={[5]}
                          type={"radialBar"}
                          height={350}
                          width={110}
                          options={{
                            plotOptions: {
                              radialBar: {
                                hollow: {
                                  margin: 15,
                                  size: "70%",
                                  image: "/images/icons/icon-1.jpg",
                                  imageWidth: 80,
                                  imageHeight: 80,
                                  imageClipped: false,
                                },
                                dataLabels: {
                                  show: true,
                                  name: {
                                    offsetY: -10,
                                    show: false,
                                    fontSize: "15px",
                                  },
                                  value: {
                                    fontSize: "30px",
                                    show: false,
                                  },
                                },
                              },
                            },
                            stroke: {
                              lineCap: "round",
                            },
                            colors: [
                              hexToRGB(
                                getLocalStorageItem("color-primary", "#8973EA"),
                                1
                              ),
                            ],
                            labels: ["Primary"],
                          }}
                        />
                      ) : (
                        <Loading />
                      )}
                    </div>
                    <div className="mt-auto">
                      <p className=" text-primary f-s-18 f-w-600">Primary</p>
                    </div>
                  </div>
                  <div className="d-flex align-items-center flex-column">
                    <div className="mt-auto">
                      {ApexCharts ? (
                        <ApexCharts
                          series={[67]}
                          type={"radialBar"}
                          height={"172"}
                          width={"200"}
                          options={{
                            plotOptions: {
                              radialBar: {
                                hollow: {
                                  margin: 15,
                                  size: "70%",
                                  image: "/images/icons/icon-2.png",
                                  imageWidth: 90,
                                  imageHeight: 90,
                                  imageClipped: false,
                                },
                                dataLabels: {
                                  show: true,
                                  name: {
                                    offsetY: -10,
                                    show: false,
                                    color: hexToRGB(
                                      getLocalStorageItem(
                                        "color-secondary",
                                        "#626263"
                                      ),
                                      1
                                    ),
                                    fontSize: "13px",
                                  },
                                  value: {
                                    color: hexToRGB(
                                      getLocalStorageItem(
                                        "color-secondary",
                                        "#626263"
                                      ),
                                      1
                                    ),
                                    fontSize: "30px",
                                    show: false,
                                  },
                                },
                              },
                            },
                            stroke: {
                              lineCap: "round",
                            },
                            colors: [
                              hexToRGB(
                                getLocalStorageItem(
                                  "color-secondary",
                                  "#8B8476"
                                ),
                                1
                              ),
                            ],
                            labels: ["Secondary"],
                          }}
                        />
                      ) : (
                        <Loading />
                      )}
                    </div>
                    <div className="mt-auto">
                      <p className=" text-secondary f-s-18 f-w-600 ">
                        secondary
                      </p>
                    </div>
                  </div>
                  <div className="d-flex align-items-center flex-column">
                    <div className="mt-auto">
                      {ApexCharts ? (
                        <ApexCharts
                          series={[57]}
                          height={"190"}
                          width={"200"}
                          type={"radialBar"}
                          options={{
                            plotOptions: {
                              radialBar: {
                                hollow: {
                                  margin: 15,
                                  size: "70%",
                                  image: "/images/icons/icon-3.png",
                                  imageWidth: 90,
                                  imageHeight: 90,
                                  imageClipped: false,
                                },
                                dataLabels: {
                                  show: true,
                                  name: {
                                    offsetY: -10,
                                    show: false,
                                    color: "rgba(var(--success),1)",
                                    fontSize: "13px",
                                  },
                                  value: {
                                    color: "rgba(var(--success),1)",
                                    fontSize: "30px",
                                    show: false,
                                  },
                                },
                              },
                            },
                            stroke: {
                              lineCap: "round",
                            },
                            colors: ["rgba(var(--success),1)"],
                            labels: ["Success"],
                          }}
                        />
                      ) : (
                        <Loading />
                      )}
                    </div>
                    <div className="mt-auto">
                      <p className=" text-success f-s-18 f-w-600 ">Success</p>
                    </div>
                  </div>
                  <div className="d-flex align-items-center flex-column">
                    <div className="mt-auto">
                      {ApexCharts ? (
                        <ApexCharts
                          series={[78]}
                          height={"210"}
                          width={"200"}
                          type={"radialBar"}
                          options={{
                            plotOptions: {
                              radialBar: {
                                hollow: {
                                  margin: 15,
                                  size: "65%",
                                  image: "/images/icons/icon-4.png",
                                  imageWidth: 110,
                                  imageHeight: 110,
                                  imageClipped: false,
                                },
                                dataLabels: {
                                  show: true,
                                  name: {
                                    offsetY: -10,
                                    show: false,
                                    color: "rgba(var(--danger),1)",
                                    fontSize: "13px",
                                  },
                                  value: {
                                    color: "rgba(var(--danger),1)",
                                    fontSize: "30px",
                                    show: false,
                                  },
                                },
                              },
                            },
                            stroke: {
                              lineCap: "round",
                            },
                            colors: ["rgba(var(--danger),1)"],
                            labels: ["Danger"],
                          }}
                        />
                      ) : (
                        <Loading />
                      )}
                    </div>
                    <div className="mt-auto">
                      <p className=" text-danger f-s-18 f-w-600 ">Danger</p>
                    </div>
                  </div>
                  <div className="d-flex align-items-center flex-column">
                    <div className="mt-auto">
                      {ApexCharts ? (
                        <ApexCharts
                          series={[88]}
                          type={"radialBar"}
                          height={"230"}
                          width={"200"}
                          options={{
                            plotOptions: {
                              radialBar: {
                                hollow: {
                                  margin: 15,
                                  size: "60%",
                                  image: "/images/icons/icon-5.png",
                                  imageWidth: 110,
                                  imageHeight: 110,
                                  imageClipped: false,
                                },

                                dataLabels: {
                                  show: true,
                                  name: {
                                    offsetY: -10,
                                    show: false,
                                    color: "rgba(var(--warning),1)",
                                    fontSize: "13px",
                                  },
                                  value: {
                                    color: "rgba(var(--warning),1)",
                                    fontSize: "30px",
                                    show: false,
                                  },
                                },
                              },
                            },
                            stroke: {
                              lineCap: "round",
                            },
                            colors: ["rgba(var(--warning),1)"],
                            labels: ["Warning"],
                          }}
                        />
                      ) : (
                        <Loading />
                      )}
                    </div>
                    <div className="mt-auto">
                      <p className=" text-warning f-s-18 f-w-600 ">Warning</p>
                    </div>
                  </div>
                  <div className="d-flex align-items-center flex-column">
                    <div className="mt-auto">
                      {ApexCharts ? (
                        <ApexCharts
                          series={[95]}
                          type={"radialBar"}
                          height={"250"}
                          width={"200"}
                          options={{
                            plotOptions: {
                              radialBar: {
                                hollow: {
                                  margin: 15,
                                  size: "55%",
                                  image: "/images/icons/icon-6.png",
                                  imageWidth: 110,
                                  imageHeight: 110,
                                  imageClipped: false,
                                },
                                dataLabels: {
                                  show: true,
                                  name: {
                                    offsetY: -10,
                                    show: false,
                                    color: "rgba(var(--info),1)",
                                    fontSize: "13px",
                                  },
                                  value: {
                                    color: "rgba(var(--info),1)",
                                    fontSize: "30px",
                                    show: false,
                                  },
                                },
                              },
                            },
                            stroke: {
                              lineCap: "round",
                            },
                            colors: ["rgba(var(--info),1)"],
                            labels: ["Info"],
                          }}
                        />
                      ) : (
                        <Loading />
                      )}
                    </div>
                    <div className="mt-auto">
                      <p className=" text-info f-s-18 f-w-600 ">Info</p>
                    </div>
                  </div>
                  <div className="d-flex align-items-center flex-column">
                    <div className="mt-auto">
                      {ApexCharts ? (
                        <ApexCharts
                          series={[100]}
                          type={"radialBar"}
                          height={"280"}
                          width={"200"}
                          options={{
                            plotOptions: {
                              radialBar: {
                                hollow: {
                                  margin: 15,
                                  size: "55%",
                                  image: "/images/icons/icon-8.png",
                                  imageWidth: 120,
                                  imageHeight: 120,
                                  imageClipped: false,
                                },
                                dataLabels: {
                                  show: true,
                                  name: {
                                    offsetY: -10,
                                    show: false,
                                    color: "rgba(var(--dark),1)",
                                    fontSize: "13px",
                                  },
                                  value: {
                                    color: "rgba(var(--dark),1)",
                                    fontSize: "30px",
                                    show: false,
                                  },
                                },
                              },
                            },
                            stroke: {
                              lineCap: "round",
                            },
                            colors: ["rgba(var(--dark),1)"],
                            labels: ["dark"],
                          }}
                        />
                      ) : (
                        <Loading />
                      )}
                    </div>
                    <div className="mt-auto">
                      <p className=" text-dark f-s-18 f-w-600 ">dark</p>
                    </div>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default RadialbarChartPage;
