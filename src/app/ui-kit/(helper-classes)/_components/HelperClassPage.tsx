import React from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  borderColors,
  borderRadius,
  borderSideStyles,
  borderStyles,
  borderWidths,
  dashedBorders,
  dottedBorders,
  imageClasses,
  linkColors,
  marginList,
  marginSizes,
  paddingList,
  paddingSizes,
  rotateData,
  sideMarginList,
  sidePaddingList,
  textBackgrounds,
  textColors,
  typographyStyles,
  widthHeightData,
} from "@/Data/UiKit/HelperData/helperPageData";
import { IconBriefcase } from "@tabler/icons-react";

const HelperClassPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Helper Classes"
          title="Ui Kits"
          path={["Helper Classes"]}
          Icon={IconBriefcase}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Text Color</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  Using{" "}
                  <a href="#" className="text-decoration-underline">
                    text-*
                  </a>{" "}
                  class for text color
                </div>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-wrap gap-2">
                  {textColors.map((color) => (
                    <span key={color.class} className={`${color.class} p-2`}>
                      - .{color.label}
                    </span>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Link Color</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  Using{" "}
                  <a href="#" className="text-decoration-underline">
                    link-*
                  </a>{" "}
                  class for link color
                </div>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-wrap gap-2">
                  {linkColors.map((color) => (
                    <span key={color.class} className="p-2">
                      -{" "}
                      <a
                        href="#"
                        className={`text-decoration-underline ${color.class}`}
                      >
                        {color.label}
                      </a>
                    </span>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Text Background Color</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  Using{" "}
                  <a href="#" className="text-decoration-underline">
                    txt-bg-*
                  </a>{" "}
                  class for light background
                </div>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-wrap gap-2">
                  {textBackgrounds.map((bg) => (
                    <span key={bg.class} className={`${bg.class} p-2 b-r-22`}>
                      - .{bg.label}
                    </span>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Row>
              {typographyStyles.map((category, index) => (
                <Col xl={4} key={index}>
                  <Card className="equal-card mb-4">
                    <CardHeader>
                      <h5 className="f-w-600">{category.title}</h5>
                      <div className="text-dark mt-2 f-s-15 f-w-500">
                        using{" "}
                        <a href="#" className="text-decoration-underline">
                          {category.description}
                        </a>
                      </div>
                    </CardHeader>
                    <CardBody>
                      {category.items.map((item, idx) => (
                        <div className="p-2" key={idx}>
                          - {category.title}{" "}
                          <span className={item.class + " ms-2"}>
                            {item.label}
                          </span>
                        </div>
                      ))}
                    </CardBody>
                  </Card>
                </Col>
              ))}
            </Row>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Padding</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  Using{" "}
                  <a href="#" className="text-decoration-underline">
                    pa-*
                  </a>{" "}
                  class for Padding
                </div>
              </CardHeader>
              <CardBody>
                <div className="text-center">
                  <Row>
                    {paddingSizes.map((pad, index) => (
                      <div className="col" key={index}>
                        <p className={`border ${pad.class} b-r-22`}>
                          {pad.label}
                        </p>
                      </div>
                    ))}
                  </Row>
                </div>
                <div className="app-divider-v">
                  <h6>Padding List</h6>
                </div>
                <Row>
                  {Array(4)
                    .fill(0)
                    .map((_, colIndex) => (
                      <div className="col-md-6 col-xl-3" key={colIndex}>
                        {paddingList
                          .slice(colIndex * 10, colIndex * 10 + 10)
                          .map((pad, i) => (
                            <p key={i}>- {pad.label}</p>
                          ))}
                      </div>
                    ))}
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Side Padding</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  Using{" "}
                  <a href="#" className="text-decoration-underline">
                    pd-t-*/pd-s-*/pd-e-*/pd-b-*
                  </a>{" "}
                  class for Padding
                </div>
              </CardHeader>
              <CardBody>
                <Row>
                  {sidePaddingList.map((side, index) => (
                    <div className="col" key={index}>
                      <p
                        className={`border ${side.items[side.items.length - 1]?.class} b-r-22`}
                      >
                        {side.items[side.items.length - 1]?.label}
                      </p>
                    </div>
                  ))}
                </Row>
                <div className="app-divider-v">
                  <h6>Side Padding List</h6>
                </div>
                <Row>
                  {sidePaddingList.map((side, index) => (
                    <Col md={6} xl={3} key={index}>
                      <p>- padding {side.type.toLowerCase()}</p>
                      {side.items.map((item, i) => (
                        <p key={i}>- {item.class}</p>
                      ))}
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Margin</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  Using{" "}
                  <a href="#" className="text-decoration-underline">
                    mg-*
                  </a>{" "}
                  class for Margin
                </div>
              </CardHeader>
              <CardBody>
                <Row>
                  {marginSizes.map((margin, index) => (
                    <div className="col-auto mb-2" key={index}>
                      <div className="border b-r-22">
                        <div className={`${margin.class} txt-bg-primary`}>
                          {margin.label}
                        </div>
                      </div>
                    </div>
                  ))}
                </Row>
                <div className="app-divider-v">
                  <h6>Margin List</h6>
                </div>
                <Row>
                  {Array(4)
                    .fill(0)
                    .map((_, colIndex) => (
                      <div className="col-md-6 col-xl-3" key={colIndex}>
                        {marginList
                          .slice(colIndex * 10, colIndex * 10 + 10)
                          .map((margin, i) => (
                            <p key={i}>- {margin.label}</p>
                          ))}
                      </div>
                    ))}
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Side Margin</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  Using{" "}
                  <a href="#" className="text-decoration-underline">
                    mg-t-*/mg-s-*/mg-e-*/mg-b-*
                  </a>{" "}
                  class for Margin
                </div>
              </CardHeader>
              <CardBody>
                <Row>
                  {sideMarginList.map((side, index) => (
                    <div className="col mg-2 border b-r-22" key={index}>
                      <div className={side.items[side.items.length - 1]?.class}>
                        <p>{side.items[side.items.length - 1]?.label}</p>
                      </div>
                    </div>
                  ))}
                </Row>
                <div className="app-divider-v">
                  <h6>Side Margin List</h6>
                </div>
                <Row>
                  {sideMarginList.map((side, index) => (
                    <Col md={6} xl={3} key={index}>
                      <p>- Margin {side.type.toLowerCase()}</p>
                      {side.items.map((item, i) => (
                        <p key={i}>- {item.class}</p>
                      ))}
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Width and Height</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  using{" "}
                  <a href="#" className="text-decoration-underline">
                    w-*/h-*
                  </a>{" "}
                  class for size
                </div>
              </CardHeader>
              <CardBody>
                <div className="d-flex justify-content-between flex-wrap">
                  {widthHeightData.map(({ width, height }, index) => (
                    <div
                      key={index}
                      className={`w-${width} h-${height} b-2-secondary d-flex-center mb-2 b-r-22`}
                    >
                      {width}*{height}
                    </div>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>

          {/* Radius */}
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Radius</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  using{" "}
                  <a href="#" className="text-decoration-underline">
                    rounded*/rounded-top*/rounded-end*/rounded-bottom*/
                    rounded-start*/rounded-pill*/rounded-circle*
                  </a>{" "}
                  class for Radius
                </div>
              </CardHeader>
              <CardBody>
                <div className="app-example">
                  {imageClasses.map((cls, index) => (
                    <img
                      key={index}
                      src="/images/blog/01.jpg"
                      className={cls}
                      alt=""
                    />
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>

          {/* Border */}
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Border</h5>
                <div className="text-dark mt-2 f-s-15 f-w-500">
                  using{" "}
                  <a href="#" className="text-decoration-underline">
                    b-*
                  </a>{" "}
                  class for border
                </div>
              </CardHeader>
              <CardBody>
                {/* Border Styles */}
                <div className="app-example">
                  {borderStyles.map((cls, index) => (
                    <div key={index} className={`${cls} b-r-22`}>
                      {cls}
                    </div>
                  ))}
                </div>

                <div className="app-divider-v">
                  <h6>Side Border</h6>
                </div>
                <div className="app-example">
                  {borderSideStyles.map((cls, index) => (
                    <div key={index} className={`${cls} b-r-22`}>
                      {cls}
                    </div>
                  ))}
                </div>

                <div className="app-divider-v">
                  <h6>Border Color</h6>
                </div>
                <div className="app-example">
                  {borderColors.map((cls, index) => (
                    <div key={index} className={`${cls} p-2 b-r-22`}>
                      {cls}
                    </div>
                  ))}
                </div>

                <div className="app-divider-v">
                  <h6>Border Width</h6>
                </div>
                <div className="app-example">
                  {borderWidths.map((cls, index) => (
                    <div key={index} className={`${cls} b-r-22`}>
                      {cls}
                    </div>
                  ))}
                </div>

                <div className="app-divider-v">
                  <h6>Border Radius</h6>
                </div>
                <div className="app-example">
                  {borderRadius.map((cls, index) => (
                    <div key={index} className={`b-1-secondary ${cls}`}>
                      {cls}
                    </div>
                  ))}
                </div>

                <div className="app-divider-v">
                  <h6>Border Dashed Style</h6>
                </div>
                <div className="app-example">
                  {dashedBorders.map((cls, index) => (
                    <div key={index} className={cls}>
                      {cls}
                    </div>
                  ))}
                </div>

                <div className="app-divider-v">
                  <h6>Border Dotted Style</h6>
                </div>
                <div className="app-example">
                  {dottedBorders.map((cls, index) => (
                    <div key={index} className={cls}>
                      {cls}
                    </div>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5 className="f-w-600">Rotate</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {rotateData.map((item, index) => (
                    <Col key={index} xs={6} sm={4} md={3} lg={2}>
                      <div className={`${item.className} b-r-22`}>
                        <span>{item.degree}</span>
                      </div>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default HelperClassPage;
